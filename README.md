* I wrote this code a long time ago for a personal project and so didn't bother documenting it. Hopefully I can find time to insert proper commenting soon. 

# Ocean Surface Simulation on GPU
These assets create a realistic ocean surface for Unity with C# and compute shaders (HLSL). 

## Features:

- Very realistic surface: The displacement map is based off the method of generating water which essentially sums up a bunch of waves using the IFFT algorithm. This results in a super realistic look and is used in many movies/games. 

Note: This displacement map is very realistic, not the shader, which still needs lots of work. 

- Customizeable: Change wave direction, wave height, wind speed, etc. 

- Infinite generation: Never reach the edge of the water

- Foam map: Adds white foam at the crests of waves by taking the Jacobian. 

- Normals: Generates normals to get proper lighting (there are currently bands between grid elements but I should be able to remove these soon)

- Super optimized: I can run an infinite grid at high resolution at 200+ FPS. This is possible because implemented on GPU. 


## Notes

To control demo scenes just use WASD for movement and the mouse. 

This project was really just about creating realistic displacement, not realistic shading, as my knowledge of creating shaders is pretty limited. Hopefully I can improve the visuals at some point.

While I wrote the code, a lot of the math in this project is way above my level, especially the FFT algorithm, so a lot was heavily based on online papers/tutorials. 
