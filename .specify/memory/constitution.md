<!--
SYNC IMPACT REPORT
==================
Version change: [TEMPLATE] → 1.0.0
Bump rationale: MAJOR — initial ratification from blank template; all principles newly defined.

Modified principles:
  [PRINCIPLE_1_NAME] → I. Code Quality
  [PRINCIPLE_2_NAME] → II. Testing Standards
  [PRINCIPLE_3_NAME] → III. User Experience Consistency
  [PRINCIPLE_4_NAME] → IV. Performance Requirements
  [PRINCIPLE_5_NAME] → (removed — consolidated into 4 focused principles)

Added sections:
  - Performance Constraints (replaces [SECTION_2_NAME])
  - Development Workflow (replaces [SECTION_3_NAME])

Removed sections:
  - None (template placeholders replaced)

Templates:
  ✅ .specify/templates/plan-template.md — Constitution Check gate aligns with 4 principles;
     Complexity Tracking table references simplicity principle; no updates required.
  ✅ .specify/templates/spec-template.md — Functional Requirements + Success Criteria sections
     align with testability and UX consistency mandates; no updates required.
  ✅ .specify/templates/tasks-template.md — Phase N (Polish) includes performance optimization
     and cross-cutting tasks; aligns with performance + quality principles; no updates required.
  ✅ .specify/templates/constitution-template.md — source template; not modified.

Deferred TODOs:
  - RATIFICATION_DATE set to 2026-04-12 (today, first adoption).
  - No placeholders intentionally deferred.
-->

# ConsoleForge Constitution

## Core Principles

### I. Code Quality

Code MUST be simple. Complexity adds latency — in execution, in maintenance, in
onboarding. Every abstraction MUST earn its place.

- All code MUST pass lint and static analysis with zero warnings before merge.
- Functions MUST have a single, clearly named responsibility.
- Cyclomatic complexity per function MUST NOT exceed 10.
- Dependencies MUST be justified; prefer stdlib over third-party where equivalent.
- Dead code MUST NOT exist in main branch.
- Complexity violations MUST be documented in the plan's Complexity Tracking table
  with explicit justification and rejected simpler alternatives.

**Rationale**: Simple code is fast to read, fast to run, and fast to fix. Every
layer of unnecessary abstraction is a tax paid on every future change.

### II. Testing Standards

Tests are first-class artifacts, not afterthoughts.

- TDD is MANDATORY: tests MUST be written and confirmed failing before implementation.
- Unit tests MUST cover all public interfaces; coverage MUST NOT fall below 80%.
- Integration tests MUST cover all cross-component contracts.
- Tests MUST be independently runnable with no shared mutable state between cases.
- Flaky tests MUST be fixed or deleted; they MUST NOT be skipped indefinitely.
- Test names MUST describe behavior, not implementation (`user_can_reset_password`,
  not `test_reset_fn`).

**Rationale**: Tests define correctness. Without them, changes are guesses.

### III. User Experience Consistency

Every user-facing surface MUST behave and look consistently.

- UI components MUST derive from the shared design system; ad-hoc styling is
  prohibited.
- Error messages MUST be actionable: state what failed and what the user can do.
- Response times perceived by the user MUST conform to the thresholds in
  Section "Performance Constraints".
- Interaction patterns (navigation, confirmation dialogs, loading states) MUST
  reuse established patterns before introducing new ones.
- Accessibility (WCAG 2.1 AA) MUST be verified for all new UI surfaces.

**Rationale**: Inconsistency erodes trust. Users build mental models; violating
them causes errors and abandonment.

### IV. Performance Requirements

Performance is a feature, not an optimization pass.

- p95 response time for user-initiated actions MUST NOT exceed 200ms under
  expected load.
- Background/async operations MUST NOT block the main thread or event loop.
- Every feature MUST declare its performance budget in the spec's Success Criteria
  before implementation begins.
- Performance regressions (>10% degradation vs. baseline) MUST block merge.
- Profiling MUST be run before attributing a bottleneck — no speculative
  optimization.

**Rationale**: Latency is UX. Complexity adds latency. Keep both minimal.

## Performance Constraints

Concrete thresholds enforced across the project:

| Operation class          | p50 target | p95 hard limit |
|--------------------------|------------|----------------|
| User-initiated UI action | <100ms     | 200ms          |
| API read                 | <50ms      | 150ms          |
| API write                | <100ms     | 300ms          |
| Background job           | N/A        | No UI blocking |

Measurement MUST use production-representative load. Synthetic benchmarks alone
do not satisfy this requirement.

## Development Workflow

- **Branching**: Feature work MUST happen on a branch; direct commits to `main`
  are prohibited except for hotfixes with two-reviewer approval.
- **PR size**: PRs SHOULD be scoped to a single user story. Large PRs MUST be
  split unless atomically required.
- **Review gate**: All PRs MUST pass the Constitution Check (plan-template.md)
  before approval.
- **Definition of Done**: Code merged + tests green + performance budget met +
  design system compliance verified.
- **Complexity justification**: Any violation of Principle I MUST appear in the
  Complexity Tracking table before the PR is opened.

## Governance

This Constitution supersedes all other project practices and style guides.
Amendments require:

1. A written proposal identifying the principle affected and rationale for change.
2. Approval from at least two project maintainers.
3. A migration plan for existing code if the amendment imposes new obligations.
4. Version increment per semantic versioning rules (see below).

**Versioning policy**:
- MAJOR: Principle removal, redefinition, or backward-incompatible governance change.
- MINOR: New principle, new section, or materially expanded guidance.
- PATCH: Clarification, wording fix, non-semantic refinement.

**Compliance review**: Constitution Check MUST be performed at plan time
(before Phase 0 research) and re-checked after Phase 1 design. All PRs are
subject to Constitution compliance as a merge gate.

**Version**: 1.0.0 | **Ratified**: 2026-04-12 | **Last Amended**: 2026-04-12
