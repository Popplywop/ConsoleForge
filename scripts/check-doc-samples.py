#!/usr/bin/env python3
"""Compile the C# samples embedded in the Markdown docs.

Docs rot silently: a rename lands, the prose still compiles in the reader's head,
and the first thing a new user pastes no longer builds. This compiles the blocks
that claim to be complete, against the real ConsoleForge projects.

Opt in by putting a marker on the line before the fence:

    <!-- doccheck: program -->   a whole program, top-level statements and all
    <!-- doccheck: snippet -->   declarations only; wrapped in usings + a namespace

Unmarked blocks are fragments and are skipped, so prose stays free to show one
interesting line without inventing scaffolding around it.
"""

import re
import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "artifacts" / "doccheck"

DOCS = [
    "README.md",
    "index.md",
    "docs/introduction.md",
    "docs/getting-started.md",
    "src/ConsoleForge.SourceGen/README.md",
]

USINGS = """using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;
"""

CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <!-- Docs omit the ceremony a real app would have; don't fail them for it. -->
    <NoWarn>CS1591;CS8321</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="{repo}/src/ConsoleForge/ConsoleForge.csproj" />
    <ProjectReference Include="{repo}/src/ConsoleForge.SourceGen/ConsoleForge.SourceGen.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
"""

BLOCK = re.compile(
    r"<!--\s*doccheck:\s*(program|snippet)\s*-->\s*\n```csharp\n(.*?)```",
    re.S,
)


def collect():
    """Yield (kind, source, origin) for every marked block, in document order."""
    for rel in DOCS:
        path = REPO / rel
        if not path.exists():
            print(f"  ! missing doc: {rel}", file=sys.stderr)
            continue
        text = path.read_text(encoding="utf-8")
        for match in BLOCK.finditer(text):
            line = text.count("\n", 0, match.start()) + 1
            yield match.group(1), match.group(2), f"{rel}:{line}"


def build(name, files):
    """Compile one synthesized project. Returns True on success."""
    proj = OUT / name
    proj.mkdir(parents=True, exist_ok=True)
    (proj / f"{name}.csproj").write_text(CSPROJ.format(repo=REPO.as_posix()))
    for filename, body in files.items():
        (proj / filename).write_text(body, encoding="utf-8")

    result = subprocess.run(
        ["dotnet", "build", "--nologo", "-v", "q",
         "-p:TreatWarningsAsErrors=true", str(proj / f"{name}.csproj")],
        capture_output=True, text=True,
    )
    if result.returncode != 0:
        sys.stdout.write(result.stdout)
        sys.stdout.write(result.stderr)
    return result.returncode == 0


def main():
    blocks = list(collect())
    if not blocks:
        print("No doccheck-marked blocks found.", file=sys.stderr)
        return 1

    if OUT.exists():
        shutil.rmtree(OUT)

    programs = [(src, origin) for kind, src, origin in blocks if kind == "program"]
    snippets = [(src, origin) for kind, src, origin in blocks if kind == "snippet"]

    failures = []

    # Snippets share one assembly; a namespace per block keeps names from colliding.
    if snippets:
        files = {}
        for i, (src, origin) in enumerate(snippets):
            files[f"Snippet{i}.cs"] = (
                f"// {origin}\n{USINGS}\nnamespace DocCheck.S{i};\n\n{src}"
            )
        # An Exe needs an entry point; the snippets are declarations only.
        files["Main.cs"] = "internal static class DocCheckEntry { static void Main() { } }\n"
        label = ", ".join(o for _, o in snippets)
        if build("snippets", files):
            print(f"  ok  snippets ({len(snippets)}): {label}")
        else:
            failures.append(f"snippets: {label}")

    # Top-level statements are one-per-assembly, so each program gets its own.
    for i, (src, origin) in enumerate(programs):
        if build(f"program{i}", {"Program.cs": f"// {origin}\n{src}"}):
            print(f"  ok  program  {origin}")
        else:
            failures.append(f"program {origin}")

    print()
    if failures:
        print(f"FAILED: {len(failures)} doc sample group(s) did not compile")
        for f in failures:
            print(f"  - {f}")
        print(f"\nSynthesized projects left in {OUT.relative_to(REPO)} for inspection.")
        return 1

    # Clean on success only: a failure is worth being able to open and poke at.
    shutil.rmtree(OUT, ignore_errors=True)
    print(f"All doc samples compile ({len(programs)} program(s), {len(snippets)} snippet(s)).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
