/*
    Author: Gus Tahara-Edmonds
    Date: Summer 2019
    Purpose: Handle making the water RNG with various settings. Also call all the compute shaders to run the IFFT algorithm,
    take the Jacobian for the folding map, and pass all to the shader. 
    Note: When I wrote the code some of the math (fresnel calcs) was beyond me and so is based off online papers/tutorials
*/

using UnityEngine;

public class Ocean : MonoBehaviour {
    bool needsUpdate;

    [Header("Visuals")]
    public Material mat;                                    //material that the ocean is using (needs to be Displacement or another that can displace the vertices) 

    [Header("Resolution")]
    public int N = 64;                                      //resolution of the fft algorithm
    public int meshN = 64;                                  //resolution of the mesh (number of faces generated per axis)
    //Note: best to keep these 2 the same value

    [Header("Grid Dimensions")] 
    public int gridSize = 64;                               //actual dimension of the mesh per axis
    public int numGridsX = 1;                               //num grids (ocean planes) in the x direction
    public int numGridsZ = 1;                               //num grids in the z direction

    [Header("Wave Settings")]
    public float waveAmp = 0.0002f;                         //wave aplitude setting
    public Vector2 windDirection = new Vector2(1, 1);       //direction waves move
    public float windSpeed = 32f;                           //how fast the wind is (affects choppy appearance)
    public float choppyScale = 1f;                          //level of chop (highly affects choppy appearance and wave steepness)

    //references to mesh and grid
    Mesh mesh;
    GameObject[] oceanGrid;

    int Nplus1;
    int passes;

    int spectrumKernel;
    int horizontalFFTKernel;
    int verticalFFTKernel;
    int finalKernel;

    ComputeShader TimeSpectrumCompute;
    ComputeShader IFFTCompute;
    ComputeShader FinalCompute;


    Texture2D h0;
    Texture2D butterFlyLookupTexture;

    RenderTexture dyBuffer;
    RenderTexture dxzBuffer;
    RenderTexture slopeBuffer;

    RenderTexture displacementMap;
    RenderTexture normalMap;
    RenderTexture foldingMap;

    Texture2D fresnelLookup;

    void Start() {
        Nplus1 = N + 1;
        passes = (int)(Mathf.Log((float)N) / Mathf.Log(2.0f));

        mesh = MakeMesh();
        SetupGrid();

        GetSpectrum();

        InitSpectrumCompute();
        InitButterflyLookupTable();
        InitFFTCompute();
        InitFinalCompute();

        CreateFresnelLookUp();
    }

    private Mesh MakeMesh() {
        int meshNPlus1 = meshN + 1;
        Vector3[] vertices = new Vector3[meshNPlus1 * meshNPlus1];
        Vector3[] normals = new Vector3[meshNPlus1 * meshNPlus1];
        Vector2[] uv = new Vector2[meshNPlus1 * meshNPlus1];
        for (int i = 0, y = 0; y <= meshN; y++) {
            for (int x = 0; x <= meshN; x++, i++) {
                vertices[i] = new Vector3(x * (float)gridSize / meshN, transform.position.y, y * (float)gridSize / meshN);
                normals[i] = Vector3.up;
                uv[i] = new Vector2(x % meshN / (float)gridSize, y % meshN / (float)gridSize);
            }
        }

        int[] triangles = new int[meshN * meshN * 6];
        for (int ti = 0, vi = 0, y = 0; y < meshN; y++, vi++) {
            for (int x = 0; x < meshN; x++, ti += 6, vi++) {
                triangles[ti] = vi;
                triangles[ti + 3] = triangles[ti + 2] = vi + 1;
                triangles[ti + 4] = triangles[ti + 1] = vi + meshN + 1;
                triangles[ti + 5] = vi + meshN + 2;
            }
        }

        Mesh mesh = new Mesh { };

        if (meshN >= 256) {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;

        return mesh;
    }

    void SetupGrid() {
        foreach (Transform child in transform) {
            GameObject.Destroy(child.gameObject);
        }

        oceanGrid = new GameObject[numGridsX * numGridsZ];
        for (int x = 0; x < numGridsX; x++) {
            for (int z = 0; z < numGridsZ; z++) {
                int idx = x + z * numGridsX;

                oceanGrid[idx] = new GameObject("Ocean grid " + idx.ToString());
                oceanGrid[idx].AddComponent<MeshFilter>();
                oceanGrid[idx].AddComponent<MeshRenderer>();
                oceanGrid[idx].GetComponent<Renderer>().material = mat;
                oceanGrid[idx].GetComponent<MeshFilter>().mesh = mesh;
                oceanGrid[idx].transform.Translate(new Vector3(x * gridSize - numGridsX * gridSize / 2, 0.0f, z * gridSize - numGridsZ * gridSize / 2));
                oceanGrid[idx].transform.parent = this.transform;
            }
        }
    }

    void CreateFresnelLookUp() {
        float nSnell = 1.34f; 

        fresnelLookup = new Texture2D(512, 1, TextureFormat.Alpha8, false) {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 0
        };

        for (int x = 0; x < 512; x++) {
            float fresnel = 0.0f;
            float costhetai = (float)x / 511.0f;
            float thetai = Mathf.Acos(costhetai);
            float sinthetat = Mathf.Sin(thetai) / nSnell;
            float thetat = Mathf.Asin(sinthetat);

            if (thetai == 0.0f) {
                fresnel = (nSnell - 1.0f) / (nSnell + 1.0f);
                fresnel = fresnel * fresnel;
            }
            else {
                float fs = Mathf.Sin(thetat - thetai) / Mathf.Sin(thetat + thetai);
                float ts = Mathf.Tan(thetat - thetai) / Mathf.Tan(thetat + thetai);
                fresnel = 0.5f * (fs * fs + ts * ts);
            }

            fresnelLookup.SetPixel(x, 0, new Color(fresnel, fresnel, fresnel, fresnel));
        }

        fresnelLookup.Apply();

        mat.SetTexture("_FresnelLookUp", fresnelLookup);
    }

    #region Init Spectrum
    void GetSpectrum() {
        UnityEngine.Random.InitState(0);
        h0 = new Texture2D(N, N, TextureFormat.RGBAFloat, false);
        for (int y = 0; y < N; y++) {
            for (int x = 0; x < N; x++) {
                int index = y * N + x;

                float phillipsSpectrum = PhillipsSpectrum(x, y);
                Vector2 h = GaussianRandomVariable() * Mathf.Sqrt(phillipsSpectrum / 2.0f);
                Vector2 hminus = GaussianRandomVariable() * Mathf.Sqrt(phillipsSpectrum / 2.0f);
                h0.SetPixel(x, y, new Color(h.x, h.y, hminus.x, -hminus.y));
            }
        }

        h0.Apply();
    }
        
    Vector2 GaussianRandomVariable() {
        float x1, x2, w;
        do {
            x1 = 2.0f * UnityEngine.Random.value - 1.0f;
            x2 = 2.0f * UnityEngine.Random.value - 1.0f;
            w = x1 * x1 + x2 * x2;
        }
        while (w >= 1.0f);

        w = Mathf.Sqrt((-2.0f * Mathf.Log(w)) / w);
        return new Vector2(x1 * w, x2 * w);
    }

    float PhillipsSpectrum(int x, int y) {
        Vector2 k = 2 * Mathf.PI / N * new Vector2(2.0f * x - N, 2.0f * y - N);
        float k_length = k.magnitude;
        if (k_length < 0.000001f) return 0.0f;

        float k_length2 = k_length * k_length;
        float k_length4 = k_length2 * k_length2;

        float k_dot_w = Vector2.Dot(k.normalized, windDirection.normalized);
        float k_dot_w2 = k_dot_w * k_dot_w;

        float L = windSpeed * windSpeed / 9.81f;
        float L2 = L * L;

        return waveAmp * Mathf.Exp(-1.0f / (k_length2 * L2)) / k_length4 * k_dot_w2;
    }
    #endregion

    #region Init Butterfly Lookup
    void InitButterflyLookupTable() {
        butterFlyLookupTexture = new Texture2D(N, N, TextureFormat.RGBAFloat, false);

        for (int i = 0; i < passes; i++) {
            int nBlocks = (int)Mathf.Pow(2, passes - 1 - i);
            int nHInputs = (int)Mathf.Pow(2, i);

            for (int j = 0; j < nBlocks; j++) {
                for (int k = 0; k < nHInputs; k++) {
                    int i1, i2, j1, j2;
                    if (i == 0) {
                        i1 = j * nHInputs * 2 + k;
                        i2 = j * nHInputs * 2 + nHInputs + k;
                        j1 = BitReverse(i1);
                        j2 = BitReverse(i2);
                    }
                    else {
                        i1 = j * nHInputs * 2 + k;
                        i2 = j * nHInputs * 2 + nHInputs + k;
                        j1 = i1;
                        j2 = i2;
                    }

                    float wr = Mathf.Cos(2.0f * Mathf.PI * (float)(k * nBlocks) / (float)N);
                    float wi = Mathf.Sin(2.0f * Mathf.PI * (float)(k * nBlocks) / (float)N);

                    butterFlyLookupTexture.SetPixel(i1, i, new Color(j1, j2, wr, wi));
                    butterFlyLookupTexture.SetPixel(i2, i, new Color(j1, j2, -wr, -wi));
                }
            }
        }

        butterFlyLookupTexture.Apply();
    }

    int BitReverse(int i) {
        int j = i;
        int sum = 0;
        int W = 1;
        int M = N / 2;
        while (M != 0) {
            j = ((i & M) > M - 1) ? 1 : 0;
            sum += j * W;
            W *= 2;
            M /= 2;
        }
        return sum;
    }
    #endregion

    #region Init Compute Shaders
    void InitSpectrumCompute() {
        TimeSpectrumCompute = Resources.Load<ComputeShader>("TimeSpectrum");
        spectrumKernel = TimeSpectrumCompute.FindKernel("GetSpectrum");

        TimeSpectrumCompute.SetInt("N", N);
        TimeSpectrumCompute.SetTexture(spectrumKernel, "h0", h0);

        dyBuffer = new RenderTexture(N, N, 0) {
            enableRandomWrite = true,
            format = RenderTextureFormat.ARGBFloat
        };
        dyBuffer.Create();
        TimeSpectrumCompute.SetTexture(spectrumKernel, "Hkt_y", dyBuffer);
        dxzBuffer = new RenderTexture(N, N, 0) {
            enableRandomWrite = true,
            format = RenderTextureFormat.ARGBFloat
        };
        dxzBuffer.Create();
        TimeSpectrumCompute.SetTexture(spectrumKernel, "Hkt_xz", dxzBuffer);
        slopeBuffer = new RenderTexture(N, N, 0) {
            enableRandomWrite = true,
            format = RenderTextureFormat.ARGBFloat
        };
        slopeBuffer.Create();
        TimeSpectrumCompute.SetTexture(spectrumKernel, "slope", slopeBuffer);
    }

    void InitFFTCompute() {
        IFFTCompute = Resources.Load<ComputeShader>("IFFT");
        horizontalFFTKernel = IFFTCompute.FindKernel("PerformFFTHorizontal");
        verticalFFTKernel = IFFTCompute.FindKernel("PerformFFTVertical");

        IFFTCompute.SetInt("N", N);
        IFFTCompute.SetInt("passes", passes);

        RenderTexture pingpongdy = new RenderTexture(N, N, 0) {
            enableRandomWrite = true,
            format = RenderTextureFormat.ARGBFloat
        };
        pingpongdy.Create();
        RenderTexture pingpongdxz = new RenderTexture(N, N, 0) {
            enableRandomWrite = true,
            format = RenderTextureFormat.ARGBFloat
        };
        pingpongdxz.Create();
        RenderTexture pingpongslope = new RenderTexture(N, N, 0) {
            enableRandomWrite = true,
            format = RenderTextureFormat.ARGBFloat
        };
        pingpongslope.Create();


        IFFTCompute.SetTexture(horizontalFFTKernel, "butterflyLookupTexture", butterFlyLookupTexture);
        IFFTCompute.SetTexture(horizontalFFTKernel, "pingpongdy", pingpongdy);
        IFFTCompute.SetTexture(horizontalFFTKernel, "pingpongdxz", pingpongdxz);
        IFFTCompute.SetTexture(horizontalFFTKernel, "pingpongslope", pingpongslope);
        IFFTCompute.SetTexture(horizontalFFTKernel, "dy", dyBuffer);
        IFFTCompute.SetTexture(horizontalFFTKernel, "dxz", dxzBuffer);
        IFFTCompute.SetTexture(horizontalFFTKernel, "slope", slopeBuffer);


        IFFTCompute.SetTexture(verticalFFTKernel, "butterflyLookupTexture", butterFlyLookupTexture);
        IFFTCompute.SetTexture(verticalFFTKernel, "pingpongdy", pingpongdy);
        IFFTCompute.SetTexture(verticalFFTKernel, "pingpongdxz", pingpongdxz);
        IFFTCompute.SetTexture(verticalFFTKernel, "pingpongslope", pingpongslope);
        IFFTCompute.SetTexture(verticalFFTKernel, "dy", dyBuffer);
        IFFTCompute.SetTexture(verticalFFTKernel, "dxz", dxzBuffer);
        IFFTCompute.SetTexture(verticalFFTKernel, "slope", slopeBuffer);
    }

    void InitFinalCompute() {
        FinalCompute = Resources.Load<ComputeShader>("Final");
        finalKernel = FinalCompute.FindKernel("CSMain");

        FinalCompute.SetInt("N", N);
        FinalCompute.SetFloat("choppyScale", choppyScale);
        FinalCompute.SetTexture(finalKernel, "dy", dyBuffer);
        FinalCompute.SetTexture(finalKernel, "dxz", dxzBuffer);
        FinalCompute.SetTexture(finalKernel, "slope", slopeBuffer);

        displacementMap = new RenderTexture(N, N, 0) {
            enableRandomWrite = true,
            format = RenderTextureFormat.ARGBHalf
        };
        displacementMap.Create();
        normalMap = new RenderTexture(N, N, 0) {
            enableRandomWrite = true,
            format = RenderTextureFormat.ARGBHalf
        };
        normalMap.Create();
        foldingMap = new RenderTexture(N, N, 0) {
            enableRandomWrite = true,
            format = RenderTextureFormat.ARGBHalf
        };
        foldingMap.Create();

        FinalCompute.SetTexture(finalKernel, "displacementMap", displacementMap);
        FinalCompute.SetTexture(finalKernel, "normalMap", normalMap);
        FinalCompute.SetTexture(finalKernel, "foldingMap", foldingMap);
    }
    #endregion

    public Transform camera;

    void Update() {
        if (needsUpdate) {
            Start();

            needsUpdate = false;
        }

        transform.position = new Vector3(Mathf.RoundToInt(camera.position.x), transform.position.y, Mathf.RoundToInt(camera.position.z));

        TimeSpectrumCompute.SetFloat("t", Time.time);
        TimeSpectrumCompute.Dispatch(spectrumKernel, N / 8, N / 8, 1);

        for (int i = 0; i < passes; i++) {
            IFFTCompute.SetInt("index", i);
            IFFTCompute.Dispatch(horizontalFFTKernel, N / 8, N / 8, 1);
        }

        for (int i = 0; i < passes; i++) {
            IFFTCompute.SetInt("index", i);
            IFFTCompute.Dispatch(verticalFFTKernel, N / 8, N / 8, 1);
        }

        FinalCompute.Dispatch(finalKernel, N / 8, N / 8, 1);

        mat.SetTexture("_DispTex", displacementMap);
        mat.SetTexture("_NormalMap", normalMap);
        mat.SetTexture("_FoldingMap", foldingMap);
    }
}