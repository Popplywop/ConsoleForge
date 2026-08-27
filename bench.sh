#!/usr/bin/env bash
#
# Benchmark runner. See the Performance section of AGENTS.md.
#
#   ./bench.sh -f '*WidgetCache*'          Tier 2: short job, in-process, under a minute.
#                                          Wide error bars — answers "4x" or "about the
#                                          same", never "3%".
#
#   ./bench.sh --full -f '*Render*'        Tier 3: the full job. Required before committing
#                                          a change to Renderer, RenderContext, LayoutEngine,
#                                          LayoutSolver, CmdDispatcher, or any IWidget.Render.
#
#   ./bench.sh --full --list               List benchmark names without running anything.
#
# Release is not optional: Debug numbers are meaningless. Run Tier 3 before and after
# sequentially with nothing else on the machine — a concurrent build moves unrelated
# benchmarks by more than 10%.
#
set -euo pipefail
cd "$(dirname "$0")"

full=0
args=()

while [[ $# -gt 0 ]]; do
    case "$1" in
        --full)          full=1; shift ;;
        -f|--filter)     args+=(--filter "$2"); shift 2 ;;
        --list)          args+=(--list flat); shift ;;
        -h|--help)       sed -n '2,18p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *)               args+=("$1"); shift ;;
    esac
done

# BenchmarkSwitcher needs some filter or it prompts interactively.
if ! printf '%s\n' "${args[@]:-}" | grep -q -- '--filter\|--list'; then
    args+=(--filter '*')
fi

if [[ $full -eq 0 ]]; then
    # Short job: 3 warmup + 3 target iterations instead of ~15 each.
    # In-process: skips generating and building a project per benchmark, which is most of
    # the wall clock. Slightly less isolation, which is the trade Tier 2 accepts.
    args+=(--job short --inProcess)
fi

exec dotnet run --project tests/ConsoleForge.Benchmarks -c Release -- "${args[@]}"
