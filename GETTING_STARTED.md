# Getting Started

Step by step, from a MetaHuman in Epic's Creator to a working character in Unity.

Follow it in order the first time. Steps 3 and 4 take under a minute each and tell you whether
anything is wrong *before* you spend time on a full import.

---

## 0. What you need

- **Unity 2022.3 or newer.** Built and verified against Unity 6000.5.
- **A MetaHuman**, assembled in MetaHuman Creator, exported with the **DCC Export** pipeline.
- Nothing else. No Unreal install, no Blender, no native plugin, no build tools.

This folder (`MetaHumanBridge`) is currently sitting on your Desktop. It is not doing anything
there — step 2 puts it into a project.

---

## 1. Get the DNA out of MetaHuman Creator

1. Open your character in MetaHuman Creator.
2. Assemble it using the **DCC Export** assembly pipeline (*not* Geometry Export or Materials
   Export — those produce Unreal-native assets that are useless outside Unreal).
3. Choose the **folder** output if offered. If you only get a zip, extract it to a folder
   before continuing — nothing here reads inside a zip.

You should end up with something like:

```
MyCharacter_DCCExport/
  <something>_head.dna      <- the face: mesh, face skeleton, blend shapes, rig logic
  <something>_body.dna      <- the body: mesh, body skeleton
  *.png                     <- colour, normal, roughness, cavity maps
  *.json                    <- assembly metadata
```

File names vary between MetaHuman releases. What matters is that you have **two `.dna` files**
and **a pile of `.png`s**. If you only have one `.dna`, note which it is — head-only and
body-only imports both work, they just give you less.

> Grooms (hair, brows, lashes) and clothing are **not** in the DNA. They are separate assets on
> the Unreal side. Nothing in this folder will produce them.

---

## 2. Put the package in a Unity project

Either way works:

**Copy it in.** Drag the whole `MetaHumanBridge` folder into your project's `Assets/` folder.
Simplest, and fine if you only need it in one project.

**Add it as a local package.** *Window > Package Manager > + > Add package from disk...* and
select `MetaHumanBridge/package.json`. Better if you want to use it across several projects —
one copy, shared.

Wait for Unity to finish compiling. There should be no console errors. If you see any, stop
here — everything downstream depends on this compiling cleanly.

---

## 3. Verify the reader (30 seconds)

**Window > MetaHuman Bridge Self Test**

This parses a complete DNA 2.8 file embedded in the package — assembled from the byte fixtures
in Epic's own OpenRigLogic test suite — and compares every decoded field against the values
Epic's reference declares. Descriptor, coordinate system, rotation sequence and signs, winding
order, control and joint names, LOD mappings, the conditional table, the PSD matrix, all four
joint-group sub-matrices and their dimensions, the geometry.

You want a green console message ending in:

```
All checks passed - the DNA reader matches Epic's reference fixture.
```

If it fails, the parser is broken and no import will be correct — that is a bug to fix, not a
setting to tweak. (The same button lives on the *Inspect DNA* tab of the main window.)

---

## 4. Inspect your DNA before importing

**Window > MetaHuman Bridge > Inspect DNA tab**

Pick your **head** `.dna` and press **Read**. This decodes the file and reports what is in it
without creating a single asset. Two reasons to always do this first: it confirms your file
parses, and it tells you the three values that determine whether the import will look right.

Note these down from the report:

| Field | Typical | What it means for you |
|---|---|---|
| **Units** | `Centimetre, Degrees` | Centimetres get scaled to metres automatically. Leave *Extra scale* at 1. |
| **Axes** | `X Right, Y Up, Z Front` | This matches Unity exactly, so no conversion happens. Anything else is handled too, but is worth knowing about if the result looks mirrored. |
| **Winding** | `CounterClockwise` | Combined with the axes, decides whether triangles get flipped. Handled automatically. |

Also sanity-check **Joints**, **Meshes** and **Blend shape channels** are non-zero, and look at
the per-mesh list at the bottom — you should see entries like `head_lod0_mesh`,
`teeth_lod0_mesh`, `eyeLeft_lod0_mesh` with plausible vertex counts.

Repeat for the **body** `.dna`. Its joint names are what the Humanoid avatar is built from.

---

## 5. First import — keep it minimal

**Window > MetaHuman Bridge > Import tab**

Fill in:

| Field | First run |
|---|---|
| Head DNA | your head `.dna` |
| Body DNA | your body `.dna` |
| Texture folder | the export folder (searched recursively) |
| Character name | e.g. `Ada` |
| Output folder | `Assets/MetaHumans` |
| **LODs** | **`0` only** |
| Create LODGroup | off (irrelevant with one LOD) |
| Extra scale | `1` |
| Flip UV V | **off** |
| **Blend shapes** | **None** |
| Bake RigLogic | **on** |
| Build Humanoid avatar | **on** |
| Create materials | on |
| Copy textures into project | on |

Press **Import MetaHuman**.

Blend shapes are off deliberately. The MetaHuman face is joint-driven — it animates fully
without them. They are correctives, and at LOD 0 they are enormous. Turn them on in step 9 once
everything else is confirmed working.

When it finishes you get a **Result** panel listing what was read and built, plus any warnings.
Read the warnings — they are specific, not decorative. Press **Select prefab** to jump to it.

Your project now has:

```
Assets/MetaHumans/Ada/
  Meshes/          one .asset per DNA mesh
  Materials/       one .mat per mesh
  Textures/        the copied .png maps
  Ada_RigLogic.asset
  Ada_Avatar.asset
  Ada.prefab       <- drag this into a scene
```

---

## 6. Check the result, in this order

Drag the prefab into a scene and work down the list. Each check isolates one stage, so the
first thing that looks wrong tells you which stage to fix.

1. **Scale.** The character should be roughly 1.7 units tall. 170 units means the unit
   conversion didn't apply; 0.017 means it applied twice.
2. **Orientation.** Standing upright, facing +Z. Lying down or facing backwards points at the
   coordinate system, which the *Inspect* tab already told you.
3. **Surfaces.** Rotate around the head. It should look solid. If you can see *through* the
   front of the face and the back of the skull is drawn instead, triangle winding is inverted.
4. **Skinning.** Expand the prefab, grab a bone like `upperarm_l` in the hierarchy, and rotate
   it. The arm should follow smoothly. **If the deformation is blocky or vertices tear, go to
   step 7** — this is almost always the four-bone default, not a bad import.
5. **Skeleton.** One shared hierarchy under the root. The face joints should sit *inside* the
   body skeleton, not as a second detached tree. Head joints reuse body joints of the same
   name, so there should be exactly one `head`, one `spine_04`, and so on.
6. **Textures.** Roughly right maps on roughly right meshes. Fine detail comes later.

---

## 7. Fix skinning quality (you will need this)

Unity blends only **4 bones per vertex** by default. MetaHumans use up to 8 or 12 — the
*Inspect* tab shows the exact number as *max influences*. The importer writes every influence
into the mesh, but Unity throws the extras away at render time unless you tell it not to.

**Project Settings > Quality > Skin Weights** → set to **Unlimited** (or at least *8 Bones*),
for every quality level you ship.

Per renderer, you can also set `SkinnedMeshRenderer.quality`, but the project setting is the
one that bites people.

---

## 8. Animate the body

The prefab has an `Animator` with the generated `Ada_Avatar` (Humanoid) already assigned.

1. Drop in any humanoid clip — Mixamo, the Unity Starter Assets, whatever you already use.
2. Assign an Animator Controller and press play.

It retargets because the MetaHuman body skeleton uses Unreal mannequin bone naming
(`pelvis`, `spine_01`, `clavicle_l`, `thigh_l`…), which the importer maps onto Unity's Humanoid
rig.

If the avatar was **not** built, the Result panel says exactly which bones were missing. The
usual cause is importing the head DNA alone — the head has no legs, so Unity refuses. The
character still works as a Generic rig.

---

## 9. Drive the face

Select the prefab root. The **MetaHuman Rig** component is already wired to `Ada_RigLogic`.

Component settings:

- **Lod** — rig evaluation level. Higher evaluates fewer rows and costs less.
- **Solve Every Frame** — on by default. Turn off to call `Solve()` yourself.
- **Apply Joints** / **Apply Blend Shapes** — useful for isolating problems.

To find out what controls exist, select `Ada_RigLogic.asset` and read **Gui Control Names**.
These follow Epic's MetaHuman Facial Description Standard, so names look like
`CTRL_expressions_browLateralL`.

```csharp
var rig = character.GetComponent<MetaHumanRig>();

// By name
rig.SetControl("CTRL_expressions_jawOpen", 1f);

// Or write the control vector directly and solve on your own schedule
RigLogicSolver solver = rig.Solver;
solver.GuiControls[12] = 0.8f;
solver.Evaluate();
```

Nothing moving? Check, in order: *Bake RigLogic* was on at import; the Result panel didn't warn
about unresolved rig joints; the control name actually exists in the asset (`SetControl`
returns `false` if it doesn't); *Apply Joints* is on.

---

## 10. Turn on the expensive things

Only once steps 6–9 are all good.

**More LODs.** Re-import with LODs `0 1 2 3` ticked and *Create LODGroup* on. Meshes are
attributed to their highest-detail LOD and grouped automatically.

**Blend shapes.** Re-import with *Blend shapes* set to **All**. Be deliberate: Unity stores
blend shape frames densely, and a LOD 0 head with several hundred correctives can push one mesh
past **200 MB**. If you want them, importing them at LOD 1 or 2 instead of LOD 0 is usually the
right trade.

Each re-import writes to a fresh uniquely-named folder rather than overwriting, so you can
compare and delete the one you don't want.

---

## 11. Materials

What you get is **correctly wired, not visually finished**. Base colour, normal and occlusion
maps land on a URP/Lit or Standard material. That is a starting point.

What you will want to do yourself:

- **Skin.** Unreal uses a dedicated shading model — subsurface scattering, dual-lobe specular,
  micro-normal blending. Point the skin materials at a proper skin shader and re-wire the maps.
- **Roughness.** MetaHuman ships roughness; Unity wants *smoothness*, which is its inverse. The
  importer deliberately does not guess. Invert the map, or use a shader that takes roughness.
- **Eyes.** Refraction and the corneal bulge need a dedicated eye shader.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Not a DNA file: missing 'DNA' signature` | Wrong file, or a zip that was never extracted | Extract the archive; pick the actual `.dna` |
| `Unsupported DNA generation` | Format newer than this reader | Check Epic's release notes; the reader covers generation 2, versions 2.1–2.8 |
| Character is 100× too big or small | Extra scale being applied on top of the unit conversion | Set *Extra scale* to `1`; the *Inspect* tab shows the DNA's declared units |
| Blocky deformation, tearing at joints | Unity's 4-bone default | Step 7 |
| Face visible from inside, solid from outside | Triangle winding | Re-check *Winding* and *Axes* on the *Inspect* tab; report it, since these are derived automatically |
| Textures vertically mirrored | UV origin convention | Re-import with *Flip UV V* on |
| `Skeleton is missing bones Unity requires` | No body DNA | Import the body DNA too, or accept a Generic rig |
| Some meshes missing | Their LOD wasn't selected | Tick more LODs |
| Face doesn't move | See step 9 |  |
| Correctives softer than in Unreal | RBF / machine-learned / twist-swing layers aren't evaluated | Known limit; the Result panel names which layers your DNA carries |
| Editor stalls or runs out of memory | Blend shapes set to *All* at LOD 0 | Set to *None*, or import them at a lower LOD |

---

## If it goes badly wrong

There is a fallback that definitely works, at the cost of needing Unreal: assemble the
MetaHuman in Unreal, export the skeletal mesh to FBX, import that into Unity. You get geometry,
skeleton and blend shapes with no custom tooling — and no rig logic, so the face is limited to
whatever blend shapes survived the trip.

Use it to unblock yourself on geometry while the DNA path gets fixed, not as the destination.

---

## Reference

- `README.md` in this folder — what Epic shipped, licensing, verification status, known limits.
- [EpicGames/OpenRigLogic](https://github.com/EpicGames/OpenRigLogic) — the DNA format and
  RigLogic solver, MIT licensed. The source of truth if anything here disagrees with reality.
- [metahuman.com/license](https://www.metahuman.com/license) — read this before shipping.
