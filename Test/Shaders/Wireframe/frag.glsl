#version 330 core

out vec4 FragColor; // Declare the output fragment color

uniform vec4 uBaseColor;    // Uniform variable for the base RGBA color to be used

// This shader simply outputs a solid color with no texturing or lighting.
void main()
{
    // Set the output fragment color to the value provided in uBaseColor
    FragColor = uBaseColor;
}