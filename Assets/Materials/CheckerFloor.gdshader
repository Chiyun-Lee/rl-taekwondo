shader_type spatial;
render_mode diffuse_lambert, specular_disabled;

uniform float square_size : hint_range(0.1, 10.0) = 1.0;
uniform vec4 color_a : source_color = vec4(0.85, 0.85, 0.85, 1.0);
uniform vec4 color_b : source_color = vec4(0.25, 0.25, 0.25, 1.0);

void fragment() {
	vec3 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	ivec2 cell = ivec2(floor(world.xz / square_size));
	bool even = (cell.x + cell.y) % 2 == 0;
	ALBEDO = (even ? color_a : color_b).rgb;
}
