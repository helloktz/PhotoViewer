const float blockSize = 4.0;

float hash(vec2 p) {
  vec3 p3 = fract(vec3(p.xyx) * 0.1031);
  p3 += dot(p3, p3.yzx + 33.33);
  return fract((p3.x + p3.y) * p3.z);
}

vec4 transition(vec2 uv) {

  float threshold = hash(floor(uv * resolution / blockSize));
  return threshold < progress ? getToColor(uv) : getFromColor(uv);
}
