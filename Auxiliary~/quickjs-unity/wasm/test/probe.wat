;; The module wasm/test/run.sh builds with build-module.sh and runs through
;; the engine: an import, memory read and written from both sides, a trap,
;; growth, and every value type the table carries.
(module
  (import "env" "log" (func $log (param i32) (result i32)))
  (memory (export "memory") 1 4)
  (func (export "add") (param i32 i32) (result i32) local.get 0 local.get 1 i32.add)
  (func (export "callLog") (param i32) (result i32) local.get 0 call $log i32.const 1 i32.add)
  (func (export "store") (param i32 i32) local.get 0 local.get 1 i32.store8)
  (func (export "load") (param i32) (result i32) local.get 0 i32.load8_u)
  (func (export "oob") (result i32) i32.const 0x7fffffff i32.load)
  (func (export "grow") (param i32) (result i32) local.get 0 memory.grow)
  (func (export "wide") (param i64) (result i64) local.get 0 i64.const 1 i64.add)
  (func (export "mulf") (param f64 f32) (result f64) local.get 0 local.get 1 f64.promote_f32 f64.mul))
