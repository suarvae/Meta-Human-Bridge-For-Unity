# MetaHuman Bridge

Imports Epic MetaHuman characters into Unity directly from the `.dna` files a MetaHuman
Creator **DCC Export** produces. No Unreal Engine, no FBX round trip, no native plugin.

---

## Why this is now possible

Two separate changes had to land, and both have:

**1. The licence stopped tying MetaHumans to Unreal (June 2025).**
Epic reclassified MetaHuman characters and animation as *non-engine products*. They can be
used in projects built with other engines — Unity, Godot — and sold on marketplaces, without
the 5% Unreal royalty, because the royalty only ever applied to engine products. MetaHuman
itself remains under the Unreal Engine EULA umbrella, and the AI carve-out still stands: you
may use MetaHumans in workflows that involve AI, but not to train or improve AI models.
Check the current text at <https://www.metahuman.com/license> before shipping — this is a
summary, not legal advice.

**2. The runtime went open source: the MetaHuman Devkit.**
Epic published <https://github.com/EpicGames/OpenRigLogic> under **MIT**. It contains:

- **DNA** — the interoperable character format. One file holds the complete description of a
  character: joints and their neutral transforms, meshes, UVs, skin weights, blend shape
  targets, animated maps and every LOD.
- **RigLogic** — the runtime facial rig solver that turns animation controls into joint
  deltas and blend shape weights, the same evaluation Unreal performs.
- The **MetaHuman Facial Description Standard**, the control-curve vocabulary that lets
  facial animation transfer between characters.

So a MetaHuman is no longer an Unreal asset you have to export *out of* — it is a documented
open format you can read directly. That is what this package does.

---

## What this package does

`MetaHuman Creator -> DCC Export -> .dna + .png` → **Unity prefab**

- Parses the DNA container in pure C# (big-endian terse archive, generation 2, versions 2.1
  through 2.8) — no native libraries, works on every platform Unity targets.
- Builds one `SkinnedMeshRenderer` per DNA mesh with full skin weights (all influences, not
  capped at four), UVs, normals and n-gon triangulation.
- Builds the skeleton once and shares it: head joints reuse body joints of the same name, so
  the face rig hangs off the body skeleton the way it does in Unreal.
- Converts coordinate space and units from whatever the DNA descriptor declares into Unity's
  (axis remap, handedness, triangle winding, centimetres to metres, Euler sequence and signs).
- Maps the Unreal-mannequin bone naming onto a Unity **Humanoid** avatar, so Mixamo and any
  humanoid animation retarget onto the body immediately.
- Optionally imports blend shape targets, wires an `LODGroup`, and creates materials with
  textures matched by name.
- Bakes the head DNA's behaviour layer into a `RigLogicAsset` and adds a `MetaHumanRig`
  component that solves it at runtime: GUI controls → conditional table → raw controls → PSD
  correctives → joint deltas + blend shape weights.

## Install

Copy this folder into your project (anywhere under `Assets/`), or add it via
**Window > Package Manager > + > Add package from disk...** and pick `package.json`.

Requires Unity 2022.3 or newer. Built and verified against Unity 6000.5.

## Use

**See [GETTING_STARTED.md](GETTING_STARTED.md) for the full step-by-step walkthrough**, including
what to verify at each stage and a troubleshooting table. The short version:

1. In MetaHuman Creator, assemble the character with the **DCC Export** pipeline and extract
   the archive. You get a head `.dna`, a body `.dna`, `.png` maps and a `.json`.
2. **Window > MetaHuman Bridge**.
3. Point *Head DNA*, *Body DNA* and *Texture folder* at the extracted files.
4. Pick LODs, set the character name and output folder, press **Import MetaHuman**.

The **Inspect DNA** tab reads a `.dna` and reports its descriptor, control counts, joint
count and per-mesh statistics without importing anything — the fastest way to confirm a file
is what you think it is.

### Driving the face at runtime

```csharp
var rig = character.GetComponent<MetaHumanRig>();

// By name, using the MetaHuman Facial Description Standard control names.
rig.SetControl("CTRL_expressions_browLateralL", 0.8f);

// Or write straight into the control vector and solve yourself.
RigLogicSolver solver = rig.Solver;
solver.GuiControls[12] = 1f;
solver.Evaluate();
```

`RigLogicAsset.guiControlNames` lists every control the character exposes. Set
`Solve every frame` off on the component if you prefer to call `Solve()` yourself.

---

## Verification status

**Verified.** The DNA reader is checked against the byte fixtures in Epic's own OpenRigLogic
test suite (`tests/dnatests/Fixturesv28.cpp` and `Fixturesv21.cpp`), comparing every decoded
field to the values their `DecodedV28` struct declares — descriptor, coordinate system,
rotation sequence and signs, winding order, control and joint names, LOD mappings, the
conditional table, the PSD matrix, all four joint-group sub-matrices and their dimensions,
and the geometry. That fixture is embedded here: **Window > MetaHuman Bridge Self Test**, or
the button on the Inspect tab, re-runs it inside your project.

**Not yet exercised against a real character.** The mesh, skeleton, avatar, material and
prefab construction compile cleanly against Unity 6000.5 but have not been run on an actual
MetaHuman export. Expect to adjust one or two things on the first import — most likely
candidates are the `Flip UV V` toggle and the material texture matching.

## Known limits

- **Behaviour layers not evaluated.** DNA files can carry RBF, machine-learned and
  twist/swing correctives (`rbfb`, `mlbh`, `twsw` and friends). The reader reports them; the
  solver does not run them. The core joint and blend shape solve — which is the bulk of the
  facial deformation — is complete. Expect correctives to be slightly softer than Unreal's.
- **Blend shape memory.** Unity stores blend shape frames densely. A LOD 0 head with several
  hundred correctives can push a single mesh past 200 MB. The default is to skip them; the
  MetaHuman face is joint driven, so it animates without them.
- **Materials are a starting point.** Unreal's skin uses a dedicated shading model with
  subsurface, dual-lobe specular and micro-normal blending. A URP/Lit material with the maps
  plugged in is correctly wired, not visually equivalent. Roughness maps in particular are
  inverted relative to Unity's smoothness convention and are left for you to author.
- **Hair and clothing are not in the DNA.** A `.dna` describes the head and body meshes.
  Grooms and garments are separate assets on the Unreal side and need their own conversion.
- **Per-frame cost.** A full face rig is several hundred joints. The solver only writes the
  joints a joint group can actually move, and the rig LOD reduces evaluated rows, but a
  crowd of MetaHumans is not free.

## Alternative route

If you would rather not go through DNA at all: assemble the MetaHuman in Unreal, export the
skeletal mesh to FBX, and import that. You get geometry, skeleton and blend shapes with no
custom tooling — but no rig logic, so the face is limited to whatever blend shapes came
across, and you are back to needing Unreal in the loop.

## Layout

```
Runtime/
  Dna/DnaTypes.cs           data model, mirrors src/dna/DNA.h
  Dna/DnaBinaryReader.cs    big-endian container parser
  Dna/DnaSpace.cs           coordinate space and unit conversion
  RigLogic/RigLogicAsset.cs baked behaviour data
  RigLogic/RigLogicSolver.cs managed port of the evaluation pipeline
  RigLogic/MetaHumanRig.cs  applies the solve to joints and blend shapes
Editor/
  Import/                   mesh, skeleton, avatar, material and asset construction
  UI/                       the editor window
  Tests/DnaSelfTest.cs      embedded golden fixture
```

## Credits

Format and evaluation semantics follow [EpicGames/OpenRigLogic](https://github.com/EpicGames/OpenRigLogic)
(MIT). No Epic code is included or linked; this is an independent implementation of the
documented format.
