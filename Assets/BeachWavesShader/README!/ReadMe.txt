=== THANKS ===

Thank YOU so much for supporting a fellow developer just like you by purchasing this asset! Please feel free to follow me on Twitter - @DomFeargrieve
Special thanks to Nintendo and the developers of Animal Crossing: New Horizons for inspiring this asset. And to Cyan (https://www.cyanilux.com) for their inspirational approach.
Extra special thanks to my wonderful wife Vix and loyal pup Ozzy <3

=== CREDIT & LEGAL ===

This asset is provided as is under a Creative Commons Attribution license. This means you are permitted to distribute, remix, adapt, and build upon this work, even commercially, with the condition that appropriate credit is given to the original creator. Read more about the license here: https://creativecommons.org/licenses/
Credit MUST be given for ANY commercial product as "Dominic Feargrieve - https://feargrieve.dev". Please include both name AND website URL. 

=== COMPATIBILITY ===

This asset has been developed and tested on Unity version 2019.3 but should work with any URP compatible Unity editor version. It is URP compatible ONLY and has been developed with 7.1.6. It has been developed with Shader Graph version 7.1.8. Your mileage may vary with newer versions of URP and Shader Graph and guarantee CANNOT be given for how well the asset works with later iterations of Unity, URP or Shader Graph.

=== SETUP ===

1. Open up the DemoScene scene and press the Play button
2. This is an example of how the shader can be used, stop Unity and in the scene view select the 'BeachTides' game object
3. Click on the 'SeaMat' material and notice the shader assigned 'SeaShoreWaves' - this is the core of the asset and the variables exposed in the shader are the aspects which can be customised to your needs
	a. FoamTeture - The foam which is applied to the very front edge of the wave. FoamTex.jpg assigned by default. Replace this as you wish but maintain the black spacing ratio as seen in the texture to ensure the correct placement of the foam bubbles.
	b. WaveSpeedMulti - The speed of the white line procedural tides.
	c. WaveNoiseScale - How much noise is applied to the wavy shape of the white procedural tides.
	d. WaveFreq - How many white procedural tide lines are seen.
	e. TideWavelength - The distance the white procedural lines are from the 'front' of the shoreline.
	f. WaveThickness - A vector2 smoothstep which determines the thickness of the white procedural lines.
	g. SeaColourDeep - The colour of the deeper part of the sea towards the 'back' of the plane.
	h. SeaColourShallow - The colour of the shallower part of the sea towards the 'front' of the plane.
	i. SandColour - The colour of the sand on the beach itself.
	j. SineMulti - The multiplier for how far up the beach the foam and wet sand waves go.
	k. RecedingWaterFactor - The multiplier for controlling how much the foam and wet sand waves recede backwards.
	l. WetSandStength - How strong the wet sand part of the effect appears.
	m. FoamThickness - The thickness of the foam bubbles.
	n. FoamFadeSpeed - How quickly the foam bubbles dissolve away.
	o. VorCellDen - The voronoi cell density for the water ripples at the back of the plane.
	p. VorPow -  The power of the voronoi cells for the water ripples at the back of the plane. The higher the value the more they fade.
	q. VorTimeMulti - How quickly the water ripples at the back of the plane move.
	r. WaveSineMulti - How much the vertices for the back of the plane are displaced for a wave effect.
	s. Wavelength - The wave size for the back of the plane wave effect.
	t. WaveSpeed - The speed at which the waves at the back of the plane move.
	u. WaveMask - A vector2 smoothstep which controls where the whole tide effect is positioned on the plane.
	v. WaterStep1 - A vector2 smoothstep which controls where the water ripples effect is positioned on the plane.
	w. WaterStep2 - A vector2 smoothstep which controls where the middle lerped water colour is positioned on the plane.
	x. WaterTexture - A texture for the water surface. WaterTex.png assigned by default.
	y. WaterTexTiling - Controls the tiling amount of the water surface texture.
	z. WaterTilingSpeed - How quickly the water surface distortion effect moves.
	aa. SandTexture - A texture for the beach sand surface which acts as a normal map. SandTex.png assigned by default. Replace this as you wish but recommend a transparent alpha channel texture set to 'Alpha Is Transparency'. 
	ab. SandNormalOffset - How offset the normals are for the sand surface.
	ac. SandNormalStrength - How strong the normal map effect appears for the sand.
	ad. SandNormalTiling - Controls the tiling amount of the sand surface texture.
4. Notice at the back of the scene is the 'OceanWater' game object.
5. Click on this and select the 'DeepSeaMat' material and notice the shader assigned 'DeepSeaShader' - this provides a trochoidal/Gerstner wave vertex displacement of a tide rolling in to the shallows of the beach. This works best when applied to a high-poly mesh plane. 'OceanWater.obj' is assigned by default.
	a. VorDensity - The voronoi cell density for the deeper ocean water ripples.
	b. VorTime - How quickly the water ripplesof the deeper ocean move.
	c. VorPower - The power of the voronoi cells for the water ripples of the deeper ocean. The higher the value the more they fade.
	d. TilingSpeed - How quickly the water surface distortion of the deeper ocean moves.
	e. Tiling - Controls the tiling amount of the water surface texture of the deeper ocean.
	f. WaterTexture - A texture for the water surface of the deeper ocean. WaterTex.png assigned by default.
	g. Color - The colour tint for the deeper water. Can be changed to HDR mode.
	h. WaveXRotate - The rotation amount on the X axis for the deeper ocean waves.
	i. WaveYRotate - The rotation amount on the Y axis for the deeper ocean waves.
	j. WaveScale - Controls the scale for the two interacting waves.
	k. WaveSpeed - The speed of the vertex displacement for the deeper ocean waves.
	l. WavePower - Controls the wave height of the deeper ocean waves.

=== SUPPORT ===

Any questions, feedback or issues can be reported to: domfeargrieve@gmail.com
I will do my best to reply in a timely manner but no assurance is given to the speed of the reply.

v1.0.0
Aug 2021