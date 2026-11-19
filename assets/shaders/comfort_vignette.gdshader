// The headset's comfort vignette (Party/ComfortVignette.cs, rules in Party/Core/ComfortRules.cs): a quad hung on the
// head that covers each eye's whole view and darkens it outside a clear circle, softly. fade dims the whole view
// (Strong's drop fade). The edge is never fully black. Drawn last, over everything, and only by the camera it hangs on.
//
// The circle is measured as an ANGLE from straight ahead in each eye's own view space (the view's −Z), not on the
// screen: an OpenXR eye's frustum is asymmetric (more field outward than toward the nose), so the screen's centre is
// not where the eye looks, and a screen-space circle would sit off-axis, mirrored between the eyes, and never fuse.
// INV_PROJECTION_MATRIX is each eye's own under multiview; the angle is taken from the pixel's ray DIRECTION (see
// fragment()), so both eyes see the same circle about their forward axis, the same angular size on every headset.
//
// The mapping from ComfortState.Radius (1 open) to angles: the clear cone's half-angle is radius × OPEN_DEGREES and
// the dark comes in over the next FEATHER_DEGREES. Radius 1 clears 75°, past the edge of any common headset's view
// (and of the flat camera's), so open is open; Recommended's floor 0.55 clears 41°, Strong's 0.35 clears 26° and is
// fully dark from 41°. These keep the proportions of the first version (clear edge at radius × 1.5 half-views, feather
// 0.3 of a half-view: one fifth of the open reach).
shader_type spatial;
render_mode unshaded, blend_mix, depth_test_disabled, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

uniform float radius : hint_range(0.0, 1.0) = 1.0;
uniform float fade : hint_range(0.0, 1.0) = 0.0;

const float OPEN_DEGREES = 75.0;
const float FEATHER_DEGREES = 15.0;
// the darkest the vignette itself gets: never full black
const float EDGE = 0.9;

varying vec2 ndc;

void vertex() {
	ndc = VERTEX.xy; // the 2 x 2 quad's corners are the view's corners in normalised device coordinates
	// a camera further than this from the quad is not the one it hangs on (another viewport, a probe): draw nothing there
	if (distance(NODE_POSITION_WORLD, CAMERA_POSITION_WORLD) > 1.0) {
		POSITION = vec4(2.0, 2.0, 2.0, 0.0); // outside every clip plane
	} else {
		POSITION = vec4(VERTEX.xy, 0.5, 1.0); // the quad fills the view, each eye's own
	}
}

void fragment() {
	// back through this eye's projection to the ray through this pixel. Its DIRECTION, not a point on it: under multiview
	// Godot bakes each eye's offset from the head into that eye's projection (view_projection[v] = projection[v] *
	// view_offset[v].inverse()), so INV_PROJECTION_MATRIX lands in the head's shared space, where the eye is not at the
	// origin. A point's angle seen from the head's centre would put each eye's clear cone ~18 deg toward the nose,
	// mirrored between the eyes. Two depths on the same ray (reverse-Z: 1.0 is the near plane, 0.5 further on) differ by
	// the ray's direction alone, whatever the eye's offset.
	vec4 near_point = INV_PROJECTION_MATRIX * vec4(ndc, 1.0, 1.0);
	vec4 far_point = INV_PROJECTION_MATRIX * vec4(ndc, 0.5, 1.0);
	vec3 ray = far_point.xyz / far_point.w - near_point.xyz / near_point.w;
	float off_axis = degrees(atan(length(ray.xy), -ray.z)); // the angle from straight ahead (the eye's −Z; the eyes look parallel)
	float inner = radius * OPEN_DEGREES;
	float v = smoothstep(inner, inner + FEATHER_DEGREES, off_axis) * EDGE;
	ALBEDO = vec3(0.0);
	ALPHA = 1.0 - (1.0 - v) * (1.0 - fade);
}
