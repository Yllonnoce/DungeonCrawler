// The headset's comfort vignette (Party/ComfortVignette.cs, rules in Party/Core/ComfortRules.cs): a quad hung on the
// head that covers each eye's whole view and darkens it outside a clear circle, softly. radius 1 is fully open (the
// clear circle reaches past the corners); smaller closes it toward the centre. fade dims the whole view (Strong's drop
// fade). The edge is never fully black. Drawn last, over everything, and only by the camera it hangs on.
shader_type spatial;
render_mode unshaded, blend_mix, depth_test_disabled, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

uniform float radius : hint_range(0.0, 1.0) = 1.0;
uniform float fade : hint_range(0.0, 1.0) = 0.0;

// radius 1 puts the clear edge at 1.5 in units of the half view: past the corners (1.41), so open is open
const float REACH = 1.5;
// the dark comes in over this much of the half view
const float FEATHER = 0.3;
// the darkest the vignette itself gets: never full black
const float EDGE = 0.9;

void vertex() {
	// a camera further than this from the quad is not the one it hangs on (another viewport, a probe): draw nothing there
	if (distance(NODE_POSITION_WORLD, CAMERA_POSITION_WORLD) > 1.0) {
		POSITION = vec4(2.0, 2.0, 2.0, 0.0); // outside every clip plane
	} else {
		POSITION = vec4(VERTEX.xy, 0.5, 1.0); // the 2 x 2 quad fills the view, each eye's own
	}
}

void fragment() {
	vec2 p = SCREEN_UV * 2.0 - 1.0;
	float inner = radius * REACH;
	float v = smoothstep(inner, inner + FEATHER, length(p)) * EDGE;
	ALBEDO = vec3(0.0);
	ALPHA = 1.0 - (1.0 - v) * (1.0 - fade);
}
