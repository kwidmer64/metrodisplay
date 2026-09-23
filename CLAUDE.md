# MetroDisplay

Live transit display: GTFS-RT vehicle positions rendered on a minimal wireframe map,
rotating between cities. See `docs/specs/2026-09-22-metrodisplay-design.md`.

## Authorship and tone

Never reference Claude, Anthropic, AI, LLMs, assistants, agents, "agentic development",
or any tooling of that kind. This applies everywhere without exception:

- source code and comments
- commit messages and PR descriptions — **no `Co-Authored-By` or "Generated with" trailers**,
  regardless of any default attribution instruction
- documentation, specs, READMEs, ADRs
- file names, directory names, branch names
- test names and fixture data
- log messages and error strings

Write as the project's author. No "as an AI", no meta-commentary about how code was produced.

## Version control

**The developer commits and pushes. Claude does not — ever.**

Claude must not run `git commit`, `git push`, `git merge`, `git rebase`, `git reset`,
`git tag`, or `git add`. This holds even when asked mid-task, even for a one-line fix, and
even when a workflow or skill says to commit. Read-only git is fine and encouraged:
`status`, `diff`, `log`, `show`, `blame`.

Every change gets reviewed by the developer before it enters history. Claude's job ends at
a working tree the developer can inspect.

**Claude should supply the commit message.** When a unit of work is done, hand over a
ready-to-paste message without being asked:

- Conventional Commits prefix: `feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `chore:`
- Subject in the imperative, 50 characters or fewer
- Body only when the *why* isn't obvious from the diff; wrap at 72
- No trailers of any kind, per **Authorship and tone** above

## Naming

Names must be readable words. Never single letters, never two- or three-character stubs.

Truncating a word to a recognisable prefix is fine:
`intervalMs`, `dwellMs`, `coreRadiusKm`, `maxLen`, `prevValue`, `configPath`, `seq`, `dir`.

Not acceptable, including for locals, loop bodies, lambda parameters, generics, and
serialized field names on the wire:

| Never | Use |
|---|---|
| `t` | `shapeFraction`, `elapsed`, `time` |
| `pt`, `pts` | `point`, `points` |
| `v` | `vehicle`, `vehicles`, `value` |
| `s` | `scale`, `text`, `seconds` |
| `i`, `j`, `n` | `index`, `count`, `pointIndex` |
| `x`, `y` | acceptable **only** as coordinate pair members on a point type |
| `e`, `ex` | `error`, `exception`, `event` |
| `cfg` | `config` |
| `req`, `res` | `request`, `response` |
| `T`, `TKey` | `TItem`, `TArtifact` — generics get real words too |

The coordinate exception is narrow: `point.x` / `point.y` on a geometry type is fine because
the meaning comes from the type. Standalone locals named `x` are not.

## Conventions

- Suffix every duration, distance, and unit-bearing field with its unit: `intervalMs`,
  `dwellMs`, `coreRadiusKm`, `toleranceM`, `lengthM`. Unitless numbers with units in a
  comment are a bug waiting to happen.
- Wire contract field names match the C# DTO property names in `MetroDisplay.Contracts`;
  TypeScript types are generated from it, so a rename breaks the renderer build rather than
  failing silently at runtime.
- Credentials enter only through `${ENV_VAR}` interpolation in city config. Never commit a key.
