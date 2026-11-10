"""Card F1: count the shader variants a Unity player build compiled, from its Editor/batch log.

Unity 6000.6 does not expose a variant count in BuildReport, but the build log prints one block per shader:

    Compiling shader "Universal Render Pipeline/Lit"
      Pass "ForwardLit" (vp)
        Target graphics API: dx11
        Full variant space:         1.769.472
        After settings filtering:   12.288
        After built-in stripping:   144
        After scriptable stripping: 48

The variants that reach the player are the "After scriptable stripping" numbers (or "After built-in stripping"
when no scriptable stripping line follows). Thousands separators depend on the OS locale and are ignored.

Usage: python -I Tools/Build/shader_variant_report.py <unity_build.log> [--top N]
"""
import re
import sys
from collections import defaultdict

SHADER = re.compile(r'^Compiling shader "(?P<name>[^"]+)"')
PASS = re.compile(r'^\s+Pass "(?P<name>[^"]*)" \((?P<stage>\w+)\)')
API = re.compile(r'^\s+Target graphics API: (?P<api>\w+)')
COUNT = re.compile(r'^\s+(?P<what>Full variant space|After settings filtering|After built-in stripping|After scriptable stripping):\s+(?P<n>[\d.,\s]+)$')


def number(text):
    digits = re.sub(r"\D", "", text)
    return int(digits) if digits else 0


def parse(path):
    per_shader = defaultdict(int)
    per_api = defaultdict(int)
    full_space = 0
    shader = stage = api = None
    pending = None  # built-in count of the current (shader, pass, api) until a scriptable count replaces it

    def flush():
        nonlocal pending
        if pending is not None and shader is not None:
            per_shader[shader] += pending
            per_api[api or "?"] += pending
        pending = None

    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.rstrip("\r\n")
            m = SHADER.match(line)
            if m:
                flush()
                shader, stage, api = m.group("name"), None, None
                continue
            if shader is None:
                continue
            m = PASS.match(line)
            if m:
                flush()
                stage = m.group("stage")
                continue
            m = API.match(line)
            if m:
                flush()
                api = m.group("api")
                continue
            m = COUNT.match(line)
            if m:
                n = number(m.group("n"))
                what = m.group("what")
                if what == "Full variant space":
                    full_space += n
                elif what == "After built-in stripping":
                    pending = n
                elif what == "After scriptable stripping":
                    pending = n
                    flush()
    flush()
    return per_shader, per_api, full_space


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    top = 15
    if "--top" in argv:
        top = int(argv[argv.index("--top") + 1])
    per_shader, per_api, full_space = parse(argv[1])
    total = sum(per_shader.values())
    print(f"log: {argv[1]}")
    print(f"shaders: {len(per_shader)} | variants after stripping (all passes, all APIs): {total:,} | full variant space: {full_space:,}")
    for api, n in sorted(per_api.items(), key=lambda kv: -kv[1]):
        print(f"  API {api}: {n:,}")
    print(f"top {top} shaders:")
    for name, n in sorted(per_shader.items(), key=lambda kv: -kv[1])[:top]:
        print(f"  {n:>8,}  {name}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
