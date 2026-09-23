---
name: source-generators
description: Using [DispatchUpdate]/[Component] in a model, and changing the ConsoleForge.SourceGen generators themselves.
triggers:
  - "DispatchUpdate"
  - "[Component]"
  - "source generator"
  - "SourceGen"
  - "CFG001"
  - "generated Update"
edges:
  - target: context/event-loop.md
    condition: for how the generated Update is driven and how components delegate
  - target: context/stack.md
    condition: for the Roslyn 4.4.0 / netstandard2.0 constraint
  - target: patterns/keybindings-and-focus.md
    condition: when the model has a static Keys KeyMap that the generated Update consults
last_updated: 2026-09-22
mex:
  id: mx_01M363AXSPQ9QVH2N0ZWD5ED0H
  type: pattern
  status: promoted
  revision: 2
  title: source-generators
  grounds_to:
    - node: method:09333acdf1b88e12b839c0c56f87f8d7
      fingerprint: mh:64:7b226d696e68617368223a5b31353737393431342c323737393737382c363039313433342c333534353038352c33393033373332382c343332373235342c363633363132322c343137323934392c32353331383030302c363034303434382c31373931303839362c363734383038362c32373239353832392c313439363835312c333534383631352c32353536373834312c333935373632332c323231363032372c383336313830312c333237303332372c31313736393938392c31353338313034352c313830393030342c333936333838312c33353934353230322c32343337323535302c323034383432372c393938373439322c353230383036372c3836373836382c36363032303931382c32303938383931322c343431393134332c31333139363439302c353936373833322c353634343739342c383033383730362c31323435323437312c353833373039372c3737393633342c36353530383733302c31303732353631392c3832363536312c31323639333039302c353630353534392c393232333838372c31353334303837332c31303835343833382c36383035383334322c363536313337362c363339373538302c32333736373130352c31373530353133362c32313331353230302c343937343134392c33353036333337362c333139363438312c313639363231362c343330333135392c393237383235332c31333731393138392c313938343038352c31303834373139312c31313732363932355d2c226e65696768626f7273223a5b226d6574686f643a3063316533343435633136353434383861333634643932386339316435366634222c226d6574686f643a3833316131303930646435313839323538353933346638343836653566313634225d2c22746f6b656e436f756e74223a3939317d
      bodyHash: 739cfa9a13f14b360b8cb90f6cc209920d813759e089cf695e429fbe6d4d169c
    - node: method:8792aa9c6ee69530069801a975ee19af
      fingerprint: mh:64:7b226d696e68617368223a5b31353737393431342c31373331333535382c363039313433342c363037383133322c33393033373332382c37363839323333362c34313437333639392c343137323934392c31303033373637352c35313039363337352c32343434343830322c373237353332352c3337303834332c313439363835312c31373030383434372c31343734353139312c3130393131343135362c33303130323934322c39393934323934362c373433303935312c3131333536333632322c34353136343332352c31373338313333312c32373031343033302c33353934353230322c35393530333536392c323034383432372c353138373738362c38323838353231382c3836373836382c36363032303931382c32303938383931322c343431393134332c37303634383735302c34333235323031372c38303930353836312c38333437393334342c36303238383134312c37383534303638302c32373938323138332c36353530383733302c33313338373030352c34303530343638372c31323639333039302c32353134353335302c35343635303630342c33343234323138352c35313139333435372c36383035383334322c3132303632313938342c343538343935362c32333736373130352c33373738363738342c3133393832373932312c343937343134392c37393130313336332c31303138313138322c333830303336382c32333936383438312c35343930343435322c353630343137312c34363333373838382c32333032383630312c31313732363932355d2c226e65696768626f7273223a5b226d6574686f643a3833316131303930646435313839323538353933346638343836653566313634225d2c22746f6b656e436f756e74223a3339397d
      bodyHash: 7041aed26f76cbe8b726cabb0c61571c424b72915822218cd8aeeef1f52df97e
    - node: method:f5026abcfc7fc5ce316c5396f42329d0
      fingerprint: mh:64:7b226d696e68617368223a5b33313832373034312c31373331333535382c32363838333739312c3135373237323433342c33393033373332382c37363839323333362c34313437333639392c343137323934392c32353331383030302c35313039363337352c32343434343830322c373237353332352c3337303834332c313439363835312c31373030383434372c31343734353139312c3130393131343135362c33303130323934322c39393934323934362c373433303935312c3131333536333632322c34353136343332352c31373338313333312c32373031343033302c33353934353230322c3131323037363234352c323034383432372c353138373738362c38323838353231382c3836373836382c36363032303931382c32303938383931322c343431393134332c37303634383735302c34333235323031372c38303930353836312c3134353331383038342c36303238383134312c37383534303638302c32373938323138332c36353530383733302c33313338373030352c34363737363635332c31323639333039302c32353134353335302c3236343339343632322c36343239323837392c35313139333435372c36383035383334322c3132303632313938342c37323137303635382c3130383635393737382c33373738363738342c3135363131323331372c343937343134392c37393130313336332c31303138313138322c333830303336382c37323137383930362c35343930343435322c353630343137312c34363333373838382c32333032383630312c31313732363932355d2c226e65696768626f7273223a5b226d6574686f643a3661313962303336343562393134376635356662323033383231383863363563225d2c22746f6b656e436f756e74223a3233337d
      bodyHash: c4a3e8a8c4500baa6294a29c05a7739868a2b03a50f689a3b3a445e675eff423
  relations:
    - type: related_to
      target: mx_01M363AXQ7MCTMRE20QDG4Q34V
      note: when the model has a static Keys KeyMap that the generated Update consults
---

# Source Generators

## Context
Attributes are declared in `src/ConsoleForge/Core/Attributes.cs`; generators live in `src/ConsoleForge.SourceGen/` (`netstandard2.0`, Roslyn 4.4.0). [`DispatchUpdateGenerator.GetTarget()`](mex://method:09333acdf1b88e12b839c0c56f87f8d7) discovers handlers and diagnostics; [`DispatchUpdateGenerator.Emit()`](mex://method:8792aa9c6ee69530069801a975ee19af) writes `{TypeName}.g.cs`. [`ComponentGenerator.Emit()`](mex://method:f5026abcfc7fc5ce316c5396f42329d0) handles `[Component]` alone; when a type has both attributes, `DispatchUpdateGenerator` handles the combination and `ComponentGenerator` skips it.

## Task: Use [DispatchUpdate] in a model

### Steps
1. Declare the model `partial` (record or class) and add `[DispatchUpdate]`.
2. For each message `FooMsg`, write `(IModel, ICmd?) OnFoo()` or `OnFoo(FooMsg msg)`. The name after `On` must start with an uppercase letter.
3. Optional: a `static` `Keys` field/property of type `KeyMap`; the generated `Update` first runs `Keys.Handle(msg)` and dispatches the mapped message instead.
4. For a sub-program add `[Component]` and implement `IComponent<TResult>` with a nullable `Result` property; `Init()` (returning null) and the explicit `IComponent<TResult>.Result` are emitted unless you wrote them.

### Gotchas
- Unmatched messages fall through to `_ => (this, null)` — no redraw, no error. A typo in a handler name silently drops a message; check for CFG002.
- Diagnostics: CFG001 missing `partial`; CFG002 handler skipped (message type not found, or a second handler for the same message — first wins); CFG003 handler return type mismatch; CFG004 type already has an `Update` (generation skipped).
- The name search (`OnFoo` → `FooMsg`) sees only the current compilation, plus a metadata fallback for `ConsoleForge.Core`/`ConsoleForge.Widgets`. Messages from any other referenced assembly resolve only when the handler takes the message as its single parameter.
- The generator emits nothing else: handlers must still return new models via `with`.

### Verify
- Build shows no CFG00x warnings for the type; a test drives `Update` with each message.

## Task: Change a generator

### Steps
1. Edit `GetTarget` (analysis) and/or `Emit` (text). Keep `GenerationTarget` / `HandlerInfo` as value-equatable records and collections in `EquatableArray<T>`, so incremental caching works.
2. New diagnostics go in `Diagnostics.cs` with the next `CFG00x` id and are listed in `AnalyzerReleases.Unshipped.md`.
3. Emit fully qualified `global::ConsoleForge.Core...` names in generated code.
4. Add a test to `tests/ConsoleForge.SourceGen.Tests/DispatchUpdateGeneratorTests.cs` (happy path + diagnostic) — it verifies emitted source.

### Gotchas
- Do not raise `Microsoft.CodeAnalysis.CSharp` above 4.4.0 or change the `netstandard2.0` target; the downgrade exists so the analyzer loads in older SDKs.
- A value in `GenerationTarget` that is not equatable (a symbol, a plain array) defeats incremental caching and re-runs the generator on every keystroke.
- PlexTui consumes the generator as an `Analyzer` `ProjectReference`; a regression shows up there as a missing `Update`.

### Verify
- `dotnet test tests/ConsoleForge.SourceGen.Tests`, then `dotnet build ConsoleForge.slnx` (samples use the generator).

## Update Scaffold
- [ ] Update `.mex/ROUTER.md` "Current Project State" if what's working/not built has changed
- [ ] Update any `.mex/context/` files that are now out of date
- [ ] If this is a new task type without a pattern, create one in `.mex/patterns/` and add to `INDEX.md`
