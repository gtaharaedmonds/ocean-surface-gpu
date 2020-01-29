using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EndlessTerrain : MonoBehaviour
{
    public Material mat;
    public Transform viewer;

    public int size = 32;
    public int LOD0 = 256;
    public int LOD1 = 128;
    public int LOD2 = 64;
    public int LOD3 = 16;

    public int LOD0Layers = 1;
    public int LOD1Layers = 1;
    public int LOD2Layers = 1;
    public int LOD3Layers = 1;

    Mesh LOD0Mesh;
    Mesh LOD1Mesh;
    Mesh LOD2Mesh;
    Mesh LOD3Mesh;

    Transform[] LOD0Objects;
    Transform[] LOD1Objects;
    Transform[] LOD2Objects;
    Transform[] LOD3Objects;

    void Start() {
        LOD0Mesh = MakeMesh(LOD0, size);
        LOD1Mesh = MakeMesh(LOD1, size);
        LOD2Mesh = MakeMesh(LOD2, size);
        LOD3Mesh = MakeMesh(LOD3, size);

        int c = 1;
        LOD0Objects = new Transform[1];
        LOD1Objects = new Transform[8 * c * LOD1Layers]; c++;
        LOD2Objects = new Transform[8 * c * LOD2Layers]; c++;
        LOD3Objects = new Transform[8 * c * LOD3Layers]; c++;

        LOD0Objects[0] = CreatePlaneObject(LOD0, 0, LOD0Mesh);

        for (int i = 0; i < LOD1Objects.Length; i++) {
            LOD1Objects[i] = CreatePlaneObject(LOD1, i, LOD1Mesh);
        }

        for (int i = 0; i < LOD2Objects.Length; i++) {
            LOD2Objects[i] = CreatePlaneObject(LOD2, i, LOD2Mesh);
        }

        for (int i = 0; i < LOD3Objects.Length; i++) {
            LOD3Objects[i] = CreatePlaneObject(LOD3, i, LOD3Mesh);
        }
    }

    int[] xOffsets1 = { 1, 1, 1, 0, -1, -1, -1, 0 };
    int[] yOffsets1 = { 1, 0, -1, -1, -1, 0, 1, 1};

    void Update() {
        int viewerIndexX = Mathf.RoundToInt(viewer.position.x / size);
        int viewerIndexY = Mathf.RoundToInt(viewer.position.z / size);

        LOD0Objects[0].position = new Vector3(viewerIndexX * size, 0, viewerIndexY * size);

        for (int i = 0; i < 8; i++) {
            int x = viewerIndexX + xOffsets1[i];
            int y = viewerIndexY + yOffsets1[i];
            print(i);
            LOD1Objects[i].position = new Vector3(x * size, 0, y * size);
        }
    }

    Mesh MakeMesh(int N, int size) {
        int NPlus1 = N + 1;
        Vector3[] vertices = new Vector3[NPlus1 * NPlus1];
        Vector3[] normals = new Vector3[NPlus1 * NPlus1];
        Vector2[] uv = new Vector2[NPlus1 * NPlus1];
        for (int i = 0, y = 0; y <= N; y++) {
            for (int x = 0; x <= N; x++, i++) {
                vertices[i] = new Vector3((float)x * size / N, transform.position.y, (float)y * size / N);
                normals[i] = Vector3.up;
                uv[i] = new Vector2(x % N / (float)size, y % N / (float)size);
            }
        }

        int[] triangles = new int[N * N * 6];
        for (int ti = 0, vi = 0, y = 0; y < N; y++, vi++) {
            for (int x = 0; x < N; x++, ti += 6, vi++) {
                triangles[ti] = vi;
                triangles[ti + 3] = triangles[ti + 2] = vi + 1;
                triangles[ti + 4] = triangles[ti + 1] = vi + N + 1;
                triangles[ti + 5] = vi + N + 2;
            }
        }

        Mesh mesh = new Mesh { };

        if (N >= 256) {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;

        return mesh;
    }

    Transform CreatePlaneObject(int LOD, int num, Mesh mesh) {
        GameObject newObj = new GameObject("LOD: " + LOD + ", #" + num);
        newObj.AddComponent<MeshFilter>();
        newObj.AddComponent<MeshRenderer>();
        newObj.GetComponent<Renderer>().material = mat;
        newObj.GetComponent<MeshFilter>().mesh = mesh;
        newObj.transform.parent = transform;
        return newObj.transform;
    }
}
