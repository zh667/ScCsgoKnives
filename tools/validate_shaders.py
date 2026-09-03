"""Compile the mod's GLSL offline exactly as Survivalcraft's Engine.Shader will see it.

Engine.Shader.PrependShaderMacros (SCAPI 1.9) rewrites the source before glCompileShader:
  #version N es          (from the source's own #version line, which it comments out)
  #define GLSL
  #define OPENGL_POSITION_FIX gl_Position.y *= u_glymul; gl_Position.z = 2.0 * gl_Position.z - gl_Position.w;
  uniform float u_glymul;            (vertex shaders only)
  #line 1
This script does the same and runs glslang (Khronos reference compiler) on the result, so
a syntax or semantic error is caught here instead of on the player's device.

    python3 tools/validate_shaders.py
"""
import os, re, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
SHADERS = os.path.join(os.path.dirname(HERE), "src", "ScCsgoKnives", "Shaders")
GLSLANG = os.path.expanduser("~/tools/glslang/bin/glslang")


def prepend(code, is_vertex):
    m = re.search(r"^#version\s+(\d+)(\s+es)?", code, re.M)
    version = int(m.group(1)) if m else 100
    is_es = bool(m and m.group(2))
    head = [f"#version {version} es" if (version >= 300 or is_es) else f"#version {version}", "#define GLSL"]
    if is_vertex:
        head.append("#define OPENGL_POSITION_FIX gl_Position.y *= u_glymul; gl_Position.z = 2.0 * gl_Position.z - gl_Position.w;")
        head.append("uniform float u_glymul;")
    head.append("#line 1")
    if m:
        code = code[:m.start()] + "// " + code[m.start():]
    return "\n".join(head) + "\n" + code


def main():
    if not os.path.exists(GLSLANG):
        print(f"glslang not found at {GLSLANG}", file=sys.stderr)
        return 2
    failed = 0
    for name in sorted(os.listdir(SHADERS)):
        if not name.endswith((".vsh", ".psh")):
            continue
        is_vertex = name.endswith(".vsh")
        code = prepend(open(os.path.join(SHADERS, name), encoding="utf-8").read(), is_vertex)
        with tempfile.NamedTemporaryFile("w", suffix=".vert" if is_vertex else ".frag", delete=False) as f:
            f.write(code)
            path = f.name
        r = subprocess.run([GLSLANG, "-S", "vert" if is_vertex else "frag", path], capture_output=True, text=True)
        os.unlink(path)
        status = "OK " if r.returncode == 0 else "ERR"
        print(f"{status} {name}")
        if r.returncode != 0:
            failed += 1
            print(r.stdout.strip())
            print(r.stderr.strip())
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
