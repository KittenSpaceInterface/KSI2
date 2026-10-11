#version 450

layout(push_constant) uniform PanelData {
    vec4 rect;
    vec4 scene;
} panel;

layout(location = 0) out vec2 maskUv;

void main() {
    vec2 corner = vec2((gl_VertexIndex & 1), (gl_VertexIndex >> 1));
    maskUv = corner;
    vec2 position = panel.rect.xy + corner * panel.rect.zw;
    gl_Position = vec4(position * 2.0 - 1.0, 0.0, 1.0);
}
