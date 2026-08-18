# [2026-08-17] v3.1.6

- WebGL: scheduler clock seeds from `performance.now()`, so timers created during boot fire at their real due times
- WebGL: a re-evaluated bootstrap retires the previous generation's timers and always captures the true browser natives
- WebGL: context teardown is refcounted and leaves the page pristine when nothing was migrated
- `clearTimeout`/`clearInterval` fall through to the browser for timers created before OneJS booted

