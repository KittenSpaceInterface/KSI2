#version 450

layout(set = 0, binding = 0) uniform sampler2D sceneColor;
layout(set = 0, binding = 1) uniform sampler2D panelMask;
layout(set = 0, binding = 2) uniform sampler2D unblurredScene;

layout(push_constant) uniform PanelData {
    vec4 rect;
    vec4 scene;
    vec4 maskSlice;
    vec4 fill;
    vec4 method;
    vec4 maskUvRect;
} panel;

layout(location = 0) in vec2 maskUv;
layout(location = 0) out vec4 outputColor;

vec3 decodeSrgb(vec3 c) {
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), greaterThan(c, vec3(0.04045)));
}
vec3 encodeSrgb(vec3 c) {
    c = max(c, vec3(0.0));
    return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, greaterThan(c, vec3(0.0031308)));
}
vec3 sampleProcessed(vec2 uv) {
    ivec2 size = textureSize(sceneColor, 0);
    vec2 p = uv * vec2(size) - 0.5;
    ivec2 i = ivec2(floor(p));
    vec2 f = fract(p);
    vec3 a = decodeSrgb(texelFetch(sceneColor, clamp(i, ivec2(0), size - 1), 0).rgb);
    vec3 b = decodeSrgb(texelFetch(sceneColor, clamp(i + ivec2(1, 0), ivec2(0), size - 1), 0).rgb);
    vec3 c = decodeSrgb(texelFetch(sceneColor, clamp(i + ivec2(0, 1), ivec2(0), size - 1), 0).rgb);
    vec3 d = decodeSrgb(texelFetch(sceneColor, clamp(i + ivec2(1, 1), ivec2(0), size - 1), 0).rgb);
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float mapSlice(float uv, float destinationCorner, float sourceCorner) {
    if (uv < destinationCorner)
        return uv * sourceCorner / destinationCorner;
    if (uv > 1.0 - destinationCorner)
        return 1.0 - (1.0 - uv) * sourceCorner / destinationCorner;
    return mix(sourceCorner, 1.0 - sourceCorner,
               (uv - destinationCorner) / (1.0 - 2.0 * destinationCorner));
}

void main() {
    vec2 panelUv = vec2(maskUv.x, mix(maskUv.y, 1.0 - maskUv.y, panel.scene.w));
    float maskAlpha;
    if (panel.maskSlice.w > 1.5) {
        maskAlpha = 1.0;
    } else {
        if (panel.maskSlice.w > 0.5) {
            panelUv.x = mapSlice(panelUv.x, panel.maskSlice.y, panel.maskSlice.x);
            panelUv.y = mapSlice(panelUv.y, panel.maskSlice.z, panel.maskSlice.x);
        }
        panelUv = panel.maskUvRect.xy + panelUv * panel.maskUvRect.zw;
        maskAlpha = texture(panelMask, panelUv).a;
    }
    if (maskAlpha <= 0.001) discard;
    if (panel.method.x > 1.5) {
        float alpha = maskAlpha * panel.fill.a;
        outputColor = vec4(panel.fill.rgb * (panel.method.y > 0.5 ? alpha : 1.0), alpha);
        return;
    }
    vec2 uv = clamp(gl_FragCoord.xy * panel.scene.xy, vec2(0.0), vec2(1.0));
    if (panel.method.x > 0.5) {
        // KSP2 draw event 17417 receives this quantized linear vertex color.
        // The serialized sRGB image tint was 24 26 2F FD before Unity converted it.
        vec3 tint = vec3(4.0, 5.0, 7.0) / 255.0;
        float alpha = maskAlpha * panel.scene.z;
        ivec2 sceneSize = textureSize(unblurredScene, 0);
        ivec2 scenePixel = clamp(ivec2(uv * vec2(sceneSize)), ivec2(0), sceneSize - 1);
        vec3 destination = texelFetch(unblurredScene, scenePixel, 0).rgb;
        vec3 tintedBlur = sampleProcessed(uv) * tint;
        // KSA's UI target is UNorm. Correct the scene contribution in linear RGB, then
        // let premultiplied blending retain any UI already drawn over that scene.
        // Exact for a scene-only destination; overlapping UI keeps encoded-space blending.
        vec3 composed = encodeSrgb(tintedBlur * alpha + decodeSrgb(destination) * (1.0 - alpha));
        vec3 source = max(composed - destination * (1.0 - alpha), vec3(0.0));
        outputColor = vec4(source, alpha);
        return;
    }
    vec3 color = vec3(0.0);
    float weightSum = 0.0;
    for (int y = -2; y <= 2; ++y) {
        for (int x = -2; x <= 2; ++x) {
            float weight = exp(-0.5 * float(x * x + y * y));
            // Keep the radius in display pixels even when scene resolution changes.
            vec2 sampleUv = clamp(uv + vec2(x, y) * 3.0 * panel.scene.xy,
                                  vec2(0.0), vec2(1.0));
            color += texture(sceneColor, sampleUv).rgb * weight;
            weightSum += weight;
        }
    }
    color /= weightSum;
    color = color / (color + vec3(1.0));
    color = pow(color, vec3(1.0 / 2.2));
    // Slightly reveal the original scene beneath the dark blurred panel.
    // KSP2 uses the mask alpha directly; this opacity is a KSI2 visual choice.
    float alpha = maskAlpha * panel.scene.z;
    const vec3 panelTint = vec3(3.0, 11.0, 25.0) / 255.0;
    outputColor = vec4(color * panelTint, alpha);
}
