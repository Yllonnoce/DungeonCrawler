// Dungeon water: two scrolling normal maps, colour by depth read from the depth buffer, foam where the bed meets
// the surface, and a little refraction of whatever lies beneath. Written for Forward+ (reversed-Z depth).
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, specular_schlick_ggx;

uniform vec4 shallow_color : source_color = vec4(0.30, 0.52, 0.55, 0.45);
uniform vec4 deep_color : source_color = vec4(0.07, 0.24, 0.30, 0.85);
uniform float glow = 0.22;
uniform vec3 foam_color : source_color = vec3(0.75, 0.85, 0.85);
uniform sampler2D normal_a : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D normal_b : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D depth_tex : hint_depth_texture, filter_linear, repeat_disable;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear, repeat_disable;
uniform vec2 flow = vec2(0.02, 0.03);
uniform float uv_scale = 0.45;
uniform float depth_fade = 0.55;
uniform float foam_width = 0.08;
uniform float refraction = 0.025;
uniform float normal_strength = 0.55;

void fragment() {
    vec2 uv = UV * uv_scale;
    vec3 n1 = texture(normal_a, uv + flow * TIME).rgb;
    vec3 n2 = texture(normal_b, uv * 1.9 - flow * TIME * 0.6 + vec2(0.37, 0.11)).rgb;
    vec3 blended = normalize(mix(n1, n2, 0.5) * 2.0 - 1.0);
    NORMAL_MAP = mix(n1, n2, 0.5);
    NORMAL_MAP_DEPTH = normal_strength;

    // how much water lies between the surface and whatever is behind it
    float depth_raw = texture(depth_tex, SCREEN_UV).r;
    vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, depth_raw);
    vec4 view = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
    view.xyz /= view.w;
    float scene_depth = -view.z;
    float here = -VERTEX.z;
    float water_depth = max(scene_depth - here, 0.0);
    float t = clamp(water_depth / depth_fade, 0.0, 1.0);
    t = t * t * (3.0 - 2.0 * t);

    vec4 col = mix(shallow_color, deep_color, t);
    float foam = 1.0 - smoothstep(0.0, foam_width, water_depth);
    foam *= 0.35 + 0.65 * (0.5 + 0.5 * sin(TIME * 2.0 + UV.x * 9.0 + UV.y * 7.0));
    vec2 offset = blended.xy * refraction * t;
    vec3 under = texture(screen_tex, clamp(SCREEN_UV + offset, vec2(0.001), vec2(0.999))).rgb;
    vec3 body = mix(under, col.rgb, col.a);
    ALBEDO = mix(body, foam_color, foam * 0.6);
    // a faint inner light so water still reads in an unlit room
    EMISSION = col.rgb * glow * (0.6 + 0.4 * t);
    ALPHA = 1.0;
    METALLIC = 0.05;
    ROUGHNESS = 0.07;
    SPECULAR = 0.7;
}
