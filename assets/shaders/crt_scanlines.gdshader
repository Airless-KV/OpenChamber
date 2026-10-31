shader_type canvas_item;

// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - CCTV monitor CRT raster scanlines
//                   - Perimeter radial lens vignetting
//                   - Top & bottom OSD translucent contrast backing for crisp text readout
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 30, 2026 | air : Streamlined CRT scanlines and vignette without return statements
// =========================================================================================

uniform float scanline_count : hint_range(50.0, 800.0) = 320.0;
uniform float scanline_intensity : hint_range(0.0, 1.0) = 0.16;
uniform float vignette_intensity : hint_range(0.0, 2.0) = 0.50;
uniform float osd_bar_opacity : hint_range(0.0, 1.0) = 0.35;

void fragment() {
	vec2 uv = UV;

	// Horizontal CRT scanline raster
	float sl = sin(uv.y * scanline_count * 3.14159265 * 2.0);
	sl = (sl + 1.0) * 0.5;
	float scanline = sl * scanline_intensity;

	// Radial lens vignetting simulating curved CRT glass perimeter
	vec2 center = uv - vec2(0.5);
	float dist = dot(center, center);
	float vignette = smoothstep(0.12, 0.62, dist) * vignette_intensity;

	// Top and bottom translucent OSD backing gradient for crystal-clear text readability
	float top_bar = smoothstep(0.18, 0.0, uv.y) * osd_bar_opacity;
	float bottom_bar = smoothstep(0.82, 1.0, uv.y) * osd_bar_opacity;
	float osd_backing = max(top_bar, bottom_bar);

	// Composite overlay darkening mask (semi-transparent black)
	float mask_alpha = clamp(scanline + vignette + osd_backing, 0.0, 0.92);
	COLOR = vec4(0.0, 0.0, 0.0, mask_alpha);
}
