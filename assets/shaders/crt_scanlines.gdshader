shader_type canvas_item;

// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - P43 green phosphor CCTV night vision screen filter
//                   - Directional light amplification and shadow gamma contrast curve
//                   - High-pass Laplacian edge sharpening for crisp enemy silhouette spotting
//                   - Dynamic photomultiplier scintillation grain and analog CRT scanlines
//                   - Perimeter radial lens vignetting and translucent OSD contrast backings
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 30, 2026 | air : Streamlined CRT scanlines and vignette without return statements
// - Nov 13, 2026 | air : Implemented P43 green phosphor night vision with edge sharpening and grain
// - Nov 14, 2026 | air : Added dynamic CRT analog glitch tearing and static snow distortion for entity vanish
// =========================================================================================

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear_mipmap;

uniform bool night_vision_enabled = true;
uniform float light_amplification : hint_range(1.0, 6.0) = 2.6;
uniform float contrast_gamma : hint_range(0.3, 1.5) = 0.72;
uniform float edge_sharpness : hint_range(0.0, 1.5) = 0.35;
uniform float grain_intensity : hint_range(0.0, 0.3) = 0.06;
uniform float glitch_intensity : hint_range(0.0, 1.0) = 0.0;

uniform vec3 nv_shadow_tint : source_color = vec3(0.015, 0.055, 0.025);
uniform vec3 nv_mid_tint : source_color = vec3(0.14, 0.88, 0.26);
uniform vec3 nv_highlight_tint : source_color = vec3(0.85, 1.0, 0.88);

uniform float scanline_count : hint_range(50.0, 800.0) = 360.0;
uniform float scanline_intensity : hint_range(0.0, 1.0) = 0.16;
uniform float vignette_intensity : hint_range(0.0, 2.0) = 0.55;
uniform float osd_bar_opacity : hint_range(0.0, 1.0) = 0.35;

void fragment() {
	vec2 uv = UV;
	vec2 screen_uv = SCREEN_UV;

	// Dynamic analog horizontal CRT scanline tearing and micro-jitter on glitch
	if (glitch_intensity > 0.0) {
		float tear_block = floor(screen_uv.y * 36.0 + TIME * 24.0);
		float tear_noise = fract(sin(tear_block * 43.17) * 43758.5453);
		if (tear_noise < glitch_intensity * 0.8) {
			float offset = (fract(tear_noise * 17.31) - 0.5) * 0.16 * glitch_intensity;
			screen_uv.x += offset;
		}
		float jitter = sin(TIME * 130.0 + screen_uv.y * 240.0) * 0.015 * glitch_intensity;
		screen_uv.x += jitter;
	}

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

	// High-frequency analog static snow burst
	float static_noise = 0.0;
	if (glitch_intensity > 0.0) {
		vec2 static_seed = uv * vec2(320.0, 180.0) + vec2(sin(TIME * 84.1), cos(TIME * 52.3));
		static_noise = fract(sin(dot(static_seed, vec2(12.9898, 78.233))) * 43758.5453);
	}

	if (!night_vision_enabled) {
		// Fallback CRT scanlines darkening over raw feed
		float mask_alpha = clamp(scanline + vignette + osd_backing, 0.0, 0.92);
		if (glitch_intensity > 0.0) {
			vec3 snow_rgb = vec3(static_noise);
			COLOR = vec4(snow_rgb, max(mask_alpha, glitch_intensity * 0.85));
		} else {
			COLOR = vec4(0.0, 0.0, 0.0, mask_alpha);
		}
	} else {
		// Sample 3D world rendered in screen texture
		vec3 scene = texture(screen_texture, screen_uv).rgb;

		// Extract perceived luminance (giving strong weight to glowing red eyes and warm lights)
		float lum = dot(scene, vec3(0.35, 0.55, 0.10));

		// High-pass edge sharpening (Laplacian) to make silhouettes of approaching enemies crisp
		vec2 px = SCREEN_PIXEL_SIZE * 1.2;
		float lum_up = dot(texture(screen_texture, screen_uv + vec2(0.0, px.y)).rgb, vec3(0.35, 0.55, 0.10));
		float lum_dn = dot(texture(screen_texture, screen_uv - vec2(0.0, px.y)).rgb, vec3(0.35, 0.55, 0.10));
		float lum_lf = dot(texture(screen_texture, screen_uv - vec2(px.x, 0.0)).rgb, vec3(0.35, 0.55, 0.10));
		float lum_rt = dot(texture(screen_texture, screen_uv + vec2(px.x, 0.0)).rgb, vec3(0.35, 0.55, 0.10));
		float laplacian = (lum * 4.0) - (lum_up + lum_dn + lum_lf + lum_rt);
		lum = clamp(lum + laplacian * edge_sharpness, 0.0, 3.0);

		// Light amplification and gamma contrast curve to pull details from dark night
		float amp = lum * light_amplification;
		float enhanced_lum = pow(clamp(amp, 0.0, 4.0), contrast_gamma);

		// Green phosphor palette mapping: deep dark shadow -> vibrant green midtone -> hot white-mint highlight
		vec3 nv_color;
		if (enhanced_lum < 0.5) {
			nv_color = mix(nv_shadow_tint, nv_mid_tint, enhanced_lum * 2.0);
		} else if (enhanced_lum < 1.0) {
			nv_color = mix(nv_mid_tint, nv_highlight_tint, (enhanced_lum - 0.5) * 2.0);
		} else {
			// Overdrive bloom for high-emission sources (monster eyes, direct lights)
			nv_color = mix(nv_highlight_tint, vec3(1.0, 1.0, 1.0), clamp(enhanced_lum - 1.0, 0.0, 1.0));
		}

		// Dynamic scintillation / photomultiplier analog electronic grain
		vec2 grain_uv = uv * vec2(480.0, 270.0);
		vec2 seed = grain_uv + vec2(sin(TIME * 43.17), cos(TIME * 67.23));
		float noise = fract(sin(dot(seed, vec2(12.9898, 78.233))) * 43758.5453);
		float grain = (noise - 0.5) * grain_intensity;
		nv_color += grain * nv_mid_tint;

		// Apply CRT scanline attenuation and lens vignette
		nv_color *= (1.0 - scanline);
		nv_color *= (1.0 - vignette);

		// Apply OSD backing darkening to preserve text contrast at top and bottom
		nv_color = mix(nv_color, nv_shadow_tint * 0.5, osd_backing * 0.7);

		// Apply glitch analog snow overlay and brightness strobing
		if (glitch_intensity > 0.0) {
			float snow = (static_noise - 0.5) * glitch_intensity * 1.5;
			float strobe = sin(TIME * 95.0) * 0.25 * glitch_intensity;
			nv_color = clamp(nv_color + vec3(snow) + vec3(strobe * 0.5), vec3(0.0), vec3(1.0));
			vec3 snow_nv = mix(vec3(static_noise), nv_mid_tint * static_noise * 1.4, 0.5);
			nv_color = mix(nv_color, snow_nv, glitch_intensity * 0.75);
		}

		COLOR = vec4(clamp(nv_color, vec3(0.0), vec3(1.0)), 1.0);
	}
}
