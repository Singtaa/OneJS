Contributing to OneJS is pretty straightforward. Usually, you can just browse through the relevant parts of the codebase tied to your PR to get a feel for how things are structured. That said, here are a few conventions we try to stick to:

> 4 spaces for indentation.
> 
> Opening brackets should be on the same line as the method name.
> 
> No `private` modifiers.
> 
> Most variable names should be in `camelCase`.
> 
> private class member variables should be prefixed with `_`.
> 
> C# methods should be in `PascalCase` unless it's meant to be used from JS in which case it can be in `camelCase` or `snake_case`.
> 
> C# types should be in `PascalCase`, even when used as JS modules.