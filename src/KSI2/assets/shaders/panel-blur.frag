#version 450

layout(set = 0, binding = 0) uniform sampler2D sceneColor;
layout(set = 0, binding = 1) uniform sampler2D panelMask;

layout(push_constant) uniform PanelData {
    vec4 rect;
    vec4 scene;
} panel;

layout(location = 0) in vec2 maskUv;
layout(location = 0) out vec4 outputColor;

void main() {
    vec2 panelUv = vec2(maskUv.x, mix(maskUv.y, 1.0 - maskUv.y, panel.scene.w));
    float maskAlpha = texture(panelMask, panelUv).a;
    if (maskAlpha <= 0.001) discard;
    vec2 uv = clamp(gl_FragCoord.xy * panel.scene.xy, vec2(0.0), vec2(1.0));
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
