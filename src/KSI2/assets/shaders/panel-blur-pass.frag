#version 450
layout(set = 0, binding = 0) uniform sampler2D sourceImage;
layout(push_constant) uniform BlurData { vec4 axisAndSize; } blur;
layout(location = 0) out vec4 outputColor;

vec3 decodeSrgb(vec3 c) {
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), greaterThan(c, vec3(0.04045)));
}
vec3 encodeSrgb(vec3 c) {
    c = max(c, vec3(0.0));
    return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, greaterThan(c, vec3(0.0031308)));
}

// UNorm targets permit KSA's storage-capable RenderTarget allocation. Decode each
// texel before interpolation to reproduce an sRGB view's linear filtering.
vec3 sampleLinear(vec2 uv) {
    ivec2 size = textureSize(sourceImage, 0);
    vec2 p = uv * vec2(size) - 0.5;
    ivec2 i = ivec2(floor(p));
    vec2 f = fract(p);
    vec3 a = decodeSrgb(texelFetch(sourceImage, clamp(i, ivec2(0), size - 1), 0).rgb);
    vec3 b = decodeSrgb(texelFetch(sourceImage, clamp(i + ivec2(1, 0), ivec2(0), size - 1), 0).rgb);
    vec3 c = decodeSrgb(texelFetch(sourceImage, clamp(i + ivec2(0, 1), ivec2(0), size - 1), 0).rgb);
    vec3 d = decodeSrgb(texelFetch(sourceImage, clamp(i + ivec2(1, 1), ivec2(0), size - 1), 0).rgb);
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

void main() {
    vec2 uv = gl_FragCoord.xy / blur.axisAndSize.zw;
    vec2 offset = blur.axisAndSize.xy / vec2(textureSize(sourceImage, 0));
    vec3 color;
    if (all(equal(offset, vec2(0.0)))) color = sampleLinear(uv);
    else {
        color = sampleLinear(uv) * 0.227027;
        color += (sampleLinear(uv + offset * 1.384615) + sampleLinear(uv - offset * 1.384615)) * 0.316216;
        color += (sampleLinear(uv + offset * 3.230769) + sampleLinear(uv - offset * 3.230769)) * 0.070270;
    }
    // Each pass stores sRGB-encoded 8-bit values, including downsampling.
    outputColor = vec4(encodeSrgb(color), 1.0);
}
