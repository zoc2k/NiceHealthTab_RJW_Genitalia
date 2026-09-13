# Nice Health Tab - RJW Genitalia

A compatibility patch that shows the reproductive body parts RimJobWorld adds on the
**Nice Health Tab** doll.

- packageId: `zoc2k.nicehealthtab.rjwgenitalia`
- Supported version: RimWorld **1.6**
- Required mods: **Nice Health Tab** and **RimJobWorld**. Without either, this patch does
  nothing at all.

---

## What it does

It puts the parts RJW adds to the body on the doll. One body part can appear both on the
**surface** (the normal doll) and as an **organ** (the organ view), so the slots are split.
Slots that point at the same body part all open the same part menu, whichever you click.

| Slot | Body part | What it draws | Layer |
|---|---|---|---|
| Chest | `Chest` | **Breasts**, per body type | body (layer 6) |
| Nipples | `Chest` | **Nipples / inverted nipples**, a layer over the breasts | body (layer 7) |
| Outer genitals | `Genitals` | **Penis** or **vulva** | body |
| Surface gonads | `Gonads` | **Testicles**, beside the penis | body |
| Anus | `Anus` | The anus inside its **window** at the lower right of the doll column | body (the window) + organ |
| Internal genitals | `Genitals` | **Vagina** | organ |
| Womb | `Genitals` | **Womb -> implantation -> fetus** | organ |
| Womb fluid | `Genitals` | **Fluid in the womb / womb inflation** | organ |
| Gonads | `Gonads` | **Testicles** | organ |
| Ovaries | `Gonads` | **Ovaries**, beside the womb | organ |

Without `RJW Now with balls! . . . and Ovaries I guess` the `Gonads` body part does not exist
at all. The gonad markers then attach to the genitals part and reference art is drawn (see
"Linked mods" below).

---

## How it looks

### Body layer - the normal doll

Breasts, nipples, the penis, the vulva and surface testicles are surface parts
(`DollBodyPart`), like the torso, arms and legs.

- **Always shown on the normal doll**, regardless of Nice Health Tab's "show organs" toggle.
- **Dropped** in the "organs" and "bones" filter views, like any other body part.
- They take part in "Show armor cover" like any other body part.

### Organ layer - the organ view

The vagina, womb, womb fluid, gonads and ovaries work exactly the way Nice Health Tab
handles the heart, liver and kidneys (`DollOrgan`).

- **Normal doll**: a marker appears only for injury, bleeding, disease, a missing part or an
  implant.
- **"Organs" filter view**: always shown and clickable, whatever the condition.
- They follow Nice Health Tab's **"show organs"** toggle.

### The anus window

**The anus is not drawn at the crotch but inside its own window.** The window sits at the
lower right of the doll column (beside the knee), with a buttocks background and an outline,
and the anus is laid on top. It is at the same place and in the same shape as the panel's anus
window, so opening the panel makes the two coincide exactly.

- The window is always visible on the normal and organ views **even when the pawn has no
  anus** (it is dropped in the bones and armour views). With no anus, or a missing one, only
  the anus glyph is left out.
- Unlike other organs the anus glyph shows **in the normal view as well**. The organ-layer
  anus is drawn there only when injured, and only while NHT's "show organs" is on, so a
  surface copy (`OuterAnus`) was added.

### The womb

- The womb is drawn for pawns with a vagina. During pregnancy the **implantation** picture
  comes first (by egg count, up to 5); past 20% gestation the **fetus** picture takes over
  (6 tiers by progress).
- With two or more babies the fetus is drawn from the **multiplet** art. Tiers without
  multiplet art fall back to the single picture. This is the same rule as RJW Menstruation's
  twin art.
- The **womb fluid** layer is laid over the womb: 6 tiers by amount, turning red when
  menstrual blood is mixed in. *Needs RJW Menstruation.*
- **Womb inflation** (vaginal cumflation), 5 tiers. *Needs RJW Menstruation - Fluids.*

### Futa and transgender pawns

Genitals and gonads go by **the parts a pawn actually has**, not by its gender.

- A pawn with both a penis and a vagina: the **penis** in the outer genitals slot, the
  **vagina and womb** in the organ view.
- A pawn with both ovaries and testicles: the **ovaries** always, the **testicles** only with
  "show testicles on futanari" turned on (off by default).

### Common

- Colours (healthy / painful / injured / critical / missing / implanted) are applied by Nice
  Health Tab.
- Marker art changes with the **size and form** of the part.
- Clicking a part lets you schedule surgery on it, exactly like any other organ.

---

## The RJW part panel

At the lower right of the doll column, above the hand and foot buttons, there is one more
button. It shows **the crotch of the pawn you are looking at**, cropped: the body and the
outer genitals (penis, vulva, testicles) only, with organs and the anus left out.

Pressing it opens a panel over the doll, shaped like the hand and foot panels.

- **Genitals box** - the genitals, drawn large, over a per-body-type background.
- **Anus window** - at the same place as the main doll's anus window.
- **Ovulation / fertilization / implantation** - RJW Menstruation's own picture, as it is.
  *Needs RJW Menstruation.*

Press again to close. While the hand and foot panel is open and covering the button's place,
the button hides underneath it. It can be switched off with "show the RJW part panel".

---

## Size hediffs

RimJobWorld stores the **size of genitals, breasts and gonads as the severity of an
always-present hediff**. Nice Health Tab treats a part with any visible hediff as not healthy,
so with no handling at all **a perfectly healthy pawn's reproductive parts would stay lit up
on the doll forever**.

This mod leaves such **plain size hediffs out of the doll's condition check only**.

- Left out: anything that is `rjw.HediffDef_SexPart` with `isBad = false` and
  `countsAsAddedPartOrImplant = false` - that is, natural genitals, breasts and gonads.
- Still shown: **bionic genitals** keep showing as implants, and pathological hediffs keep
  showing as well.
- The size information still appears **in the list on the right**, and still drives the
  size-based art.

It can be switched off in the mod settings.

---

## Mod settings

Options -> Mod settings -> **Nice Health Tab - RJW Genitalia**

The window has three tabs: **General**, **Chest & nipples** and **Gonads**.

| Tab | Setting | Default | What it does |
|---|---|---|---|
| General | **Show genitals on the child body type** | **off** | Draws this patch's markers on pawns that use the child doll. |
| General | **Show the RJW part panel** | on | The button and panel described above. |
| General | **Ignore plain size hediffs on the doll** | on | The size hediff handling above. |
| Chest & nipples | **Show chest on male pawns** | **off** | Draws the chest on pawns whose gender is male. The nipples follow it. |
| Chest & nipples | **Show nipples** | on | Draws the nipples on the normal doll. |
| Chest & nipples | **Follow skin colour** | on | Mixes the pawn's skin with the reference colour for the base colour. |
| Chest & nipples | **Follow areola pigmentation** | on | Deepens the base colour by each pawn's own depth. *Needs RJW Menstruation* |
| Chest & nipples | **Show inverted nipples** | on | Uses the dedicated art for pawns with inverted nipples. *Needs Sized Apparel* |
| Chest & nipples | **Draw in black and white** | off | Keeps only the brightness of the resolved colour. |
| Gonads | **Show testicles on futanari** | **off** | Draws testicles on pawns with both a penis and a vagina. Off leaves only the ovaries. |

The settings that are **off** by default are the ones where drawing everything as it is looks
wrong instead: RimJobWorld gives every pawn a breast part, and a futa has both ovaries and
testicles. Only the marker is hidden - the body part and its hediffs stay, so injuries,
treatment and surgery work as usual.

### Linked mods

The top of the General tab shows the **status of linked mods**: which ones were found, and
therefore which rows are locked. Hover a mod's name for a tooltip saying what it adds to this
patch and what is missing without it.

| Mod | Kind | What it adds |
|---|---|---|
| Nice Health Tab | required | The doll, the organ view, the surgery menu, the RJW part panel |
| RimJobWorld | required | The genital, breast, nipple and anus parts and their sizes; the womb and the pregnancy stages (implantation, fetus) |
| RJW Menstruation | optional | Fluid in the womb and its colour, areola pigmentation, the panel's ovulation / fertilization / implantation |
| RJW Menstruation - Fluids | optional | Womb inflation, 5 tiers |
| Sized Apparel | optional | Inverted nipples |
| RJW Now with balls! | optional | Real testicle and ovary sizes, translated gonad names, separate testicle and ovary removal surgery |

Two more mods are handled without appearing in that list:

- **Cumpilation** - stops the red `Tried to add health diff to missing part` log error on
  pawns whose genitals are missing. See "What the assembly does", item 5.
- **Licentia Serums** - when a serum swaps the breast hediff for another kind, the nipple
  colour follows (while RJW Menstruation is present).

**Without the balls mod the doll is not left empty.** Pawns with a vagina get the reference
ovary picture (`Ovaries_5`) and pawns with a penis get testicles that follow the penis size.
The gonad markers then attach to the genitals part, so clicking one opens the genitals menu.

**Saved values of locked rows are not discarded.** Add the mod back and the row returns as you
left it.

### About the child body type setting

Children suffer disease and injury too, and need quick diagnosis and treatment when they do.
This mod therefore **defines the parts on the child doll (Kid) as well** and leaves the display
to a setting. The default is **off**.

It goes by the **doll**, not by age. Nice Health Tab decides which pawns use the child doll
(under 15 by default). If you turn the child doll off in Nice Health Tab's settings, children
are drawn with adult dolls and the markers show regardless of this setting.

The setting applies to **the doll markers only**. Injuries, treatment, surgery and the list on
the right work as usual.

### Where the nipple colour and shape come from

The nipples are not painted into the breast texture but are **a separate layer laid over it**.
That way the breasts can take Nice Health Tab's health colour while the nipples take the colour
the pawn actually has.

**Shape - Sized Apparel (SAR)**

SAR attaches a `SizedApparelBodyPartDetail` comp to the part hediff and, when the part is
created, rolls one of its candidates into `variation`. The breast candidates are `default` and
`InvertedNipple`. We read that value and use **the inverted nipple art for pawns that have
them**. Without SAR every pawn gets the default nipple.

**Colour - RJW Menstruation**

RJW itself has no nipple "colour" data, only size. The colour is actually computed by RJW
Menstruation: a comp on the breast hediff holds a per-pawn **depth (alpha)** and the **colour
it deepens towards**. This mod borrows both and paints like this:

```
base   = CMYKLerp(skin colour, reference colour, 0.5)   <- reference measured from SAR nipples
nipple = CMYKLerp(base, dark colour, alpha)
alpha  = a per-pawn base value + pregnancy/nursing progress x delta
```

- The `dark colour` differs per breast kind: dark brown for natural breasts, white for
  artificial ones (Hydraulic / Bionic / Archotech).
- It deepens during pregnancy and while nursing a baby, and some of that stays permanently.
- It moves with **the same depth and in the same direction** as Menstruation's own breast UI.
  Only the starting point differs - a base colour with pink mixed in rather than plain skin -
  so at low depth it is a little pinker.
- Turning "follow skin colour" off pins the base to the reference colour; turning "follow
  areola pigmentation" off leaves the nipple at the base colour.

**Colour - Licentia Serums**

It has no feature that changes nipple colour directly. Instead a mutagenic serum swaps **the
breast hediff itself** for another one (slime breasts, for example). Since the `dark colour`
above belongs to the breast kind, a serum that changes the breasts changes the nipple colour
too, and this mod simply reads the result.

**Without Menstruation**

The pigmentation data does not exist. The deepening step is then skipped and the **base
colour** is drawn, rather than inventing a per-pawn value that is not there. The colour still
follows the pawn's skin.

The nipple layer has no click handling and no tooltip. The chest part right underneath already
carries those, so clicking selects the chest as usual.

---

## Art by sex and size

One part takes different anatomical forms from pawn to pawn, so **each form has its own art and
its own position on the doll**.

| Part | Forms | How it is told apart |
|---|---|---|
| Outer genitals | penis / vulva | the RJW hediff's `genitalFamily` (with both, the penis wins) |
| Gonads | testicles / ovaries | `Testicles` / `Ovaries` in the hediff defName (the genital family instead, without the balls mod) |
| Chest | breasts (only one form) | not told apart |
| Nipples | nipple / inverted nipple | Sized Apparel's part variation |
| Womb | womb / implantation / fetus | whether a vagina is present, and the pregnancy hediff |
| Womb fluid | fluid / inflation | RJW Menstruation's fluid amount, Fluids' inflation hediff |

Only the chest goes by gender, because RJW gives every pawn the same `Breasts` hediff.

### Penis and testicle kinds

RJW has many kinds of penis - `HorsePenis`, `DogPenis`, `DragonPenis` and so on - and the art
follows the kind, the way Sized Apparel does it.

- **Penis** - picked by the pawn's penis hediff (its defName) and its Sized Apparel part
  variation, if it has one.
- **Testicles** - picked by the pawn's **penis** kind, exactly as Sized Apparel picks balls art
  (`Penis/Balls/<penis>_<tier>`). Kind testicle art is drawn together with that kind's penis,
  so it sits where the penis is drawn. Its size follows the **testicles** when the balls mod is
  loaded, and the **penis** without it (there is no testicle size then).

The lookup for one tier, first match wins:

1. the kind with its variation - `HorsePenis_3_RNW`
2. the kind - `HorsePenis_3`
3. the default name with the variation - `Penis_3_RNW`
4. the default art - `Penis_3`

Only tiers that are actually drawn count. An empty picture is skipped and the default art is
used, so unfinished kinds never make the part disappear. A kind nobody has drawn yet (or a
modded penis this mod does not know) simply uses the default art.

Drawn kinds:

- **Penis** - CatPenis, DemonPenis, DogPenis, DragonPenis, HorsePenis, HydraulicPenis,
  OrcPenis, OvipositorM, SlimeTentacles.
- **Testicles** - HorsePenis and OrcPenis. The other kinds above have **no testicles**: their
  testicles are not drawn on the doll, in the RJW panel or on the crotch button. The organ
  view keeps the testicle marker, so injuries and surgery on the gonads still show there.

Each kind is measured on its own. Its click area and crotch button frame follow its own art,
so a long kind (the horse penis, say) does not change them for other pawns. In the RJW panel
every kind uses the usual placement; art that reaches past the genitals box is cut off at its
edge.

### Size tiers

RJW expresses a part's size as a hediff severity. That value is turned into a tier which
selects the art.

| Form | Tiers | Where the value comes from |
|---|---|---|
| Penis, vagina, vulva | **6** (0-5) | RJW size - matches RJW/balls' own tiers |
| Testicles, ovaries | **6** (0-5) | balls size - matches |
| Breasts, nipples | **7** (0-6) | the same thresholds as Sized Apparel |
| Anus | **6** (0-5) | the same thresholds as Sized Apparel and RJW |
| Implantation | **5** | the egg count on the pregnancy hediff (below 20% progress) |
| Fetus | **6** | pregnancy progress (multiplet art with two or more babies) |
| Womb fluid | **6** | RJW Menstruation's fluid amount (a fraction of womb capacity) |
| Womb inflation | **5** | Fluids' vaginal cumflation hediff |

The threshold severities live in `<sizeThresholds>` in `1.6/Defs/**/Forms_*.xml` and can be
changed in XML alone.

When a part is destroyed or harvested, **its art is not drawn**: RJW wipes every hediff on a
part the moment it goes missing, so there is no way to know what was there and nothing to
draw. For the anus, the window remains. The missing part itself still appears in the hediff
list on the right as usual.

---

## Install

1. Put the mod folder into RimWorld's `Mods/` folder.
2. Enable it in the mod list.
3. Keep the load order below.

## Load order

```
Harmony
Core / DLC
Milky Way              (if present)
Nice Health Tab
RimJobWorld
RJW Now with balls!    (if present)
-----------------------------------
Nice Health Tab - RJW Genitalia   <- must be below these
```

`loadAfter` in `About.xml` enforces this order, so it is usually correct automatically.
RJW Menstruation, Fluids, Sized Apparel and Cumpilation are looked up after startup finishes,
so their order does not matter.

---

## Compatibility

| Situation | Behaviour |
|---|---|
| No Nice Health Tab | Disabled quietly; nothing happens |
| No RimJobWorld | The Defs are not even loaded (`MayRequire`). No errors, no warnings |
| No balls mod | The gonads are drawn from reference art and attach to the genitals part. The surgery split and the translated gonad names are missing |
| An optional mod missing | Only that mod's features are missing, and the settings that need it are locked |
| Other race mods (Ratkin / Kurin / ABF Synstruct …) | Shown automatically when Nice Health Tab provides an index remap for that race and the race has the RJW parts |
| Combat Extended / Multiplayer | This mod creates no game state, so nothing special is needed |

Use textures by referencing or modifying those from other mods.

---

## Limitations and known issues

1. **An override made with Nice Health Tab's "body part assignment editor" can make this mod's
   parts disappear for that BodyDef.**
   NHT's editor is hard-coded to the 64 vanilla human parts, so a user-made override does not
   contain the reproductive entries. This mod adds its mappings to the override dictionary at
   startup as well, but **the game has to be restarted once** after creating an override.

2. **By default no markers are shown on the child doll.** Turn on "show genitals on the child
   body type" in the mod settings. It applies immediately, with no restart.

3. **Toggling "ignore plain size hediffs" takes effect after you select another pawn and come
   back**, because Nice Health Tab caches the hediff list per pawn.

4. **A part slot and its contents are different things.** In RJW, `Genitals`, `Chest` and
   `Anus` are body parts that **always exist**, regardless of gender, and the penis, vagina,
   breasts and so on are hediffs attached on top. So a pawn with no genitals still shows an
   empty part slot in the "organs" view. That is RJW's structure, and it is treated like any
   other organ.

5. **Nothing is added to the enlarged hand and foot dolls.**

6. **Tier 0 of the breasts is an empty picture** (tiers 0-1 for the Hulk body type), so
   nothing is drawn for a chest that size. For the Male body type, tier 0 of the nipples and
   inverted nipples is empty as well.

7. **Scars, bandages and the head (hair and gene overlays) are not drawn inside the RJW panel
   button.** Nice Health Tab draws those in a way that is not clipped to the button frame, so
   left alone they would spill outside it. They appear on the main doll as usual.

8. The threshold severities match values measured from the Sized Apparel source. The breasts
   work out to 12 tiers, but Sized Apparel itself keeps only 7 pictures and reuses the largest
   above that, so this mod uses 7 as well. The anus uses the same table as the genitals, so 6.
   To change them, edit `<sizeThresholds>` in `Forms_*.xml`.

9. The anus window's place is set by `AnusWindowPx` in `Source/PanelRenderer.cs` (doll column
   template pixels). The panel's own anus box has to be at the same place, and the build
   tooling cross-checks the two.

---

## Layout

```
NiceHealthTab_RJW_Genitalia/
|- About/
|  |- About.xml          modDependencies: NHT + RJW only (the rest are optional)
|  |- Preview.png
|  `- ModIcon.png
|- 1.6/
|  |- Defs/
|  |  |- Core/          Doll_{Chest,Nipples,Genitals,OuterGenitals,Womb,WombFluid,
|  |  |                       Anus,OuterAnus}.xml + Forms_Core.xml
|  |  |                 -> MayRequire="rim.job.world"
|  |  |- Gonads/        Doll_{Gonads,OuterGonads,Ovaries}.xml + Forms_Gonads.xml
|  |  |                 -> MayRequire="rim.job.world" (reference art without the balls mod)
|  |  `- BallsSurgery/  Recipes_RemoveGonads.xml - remove testicles / remove ovaries
|  |                    -> MayRequire="rim.job.world,teheeitsme525.rjwgenderorgansmod"
|  `- Assemblies/
|     `- NHT_RJW_Genitalia.dll
|- Languages/
|  `- English/Keyed/
|- Source/               C# sources plus the csproj, for rebuilding
`- Textures/
   `- HediffTab/
      |- RJW/BodyParts/                      RimJobWorld parts
      |  |- <body type>/{Breasts,Nipple,InvertedNipple}/
      |  |- Penis/ Vagina/ Vulva/ Anus/      shared
      |  |- Penis/<kind>/<kind>_<tier>.png   penis kinds (HorsePenis/, DogPenis/ ...)
      |  `- Womb/ (+ Implanted/, Fetus/)     womb, implantation, fetus
      |                                      (Fetus_<tier>_Multiplet - multiple pregnancy)
      |- RJW_Menstruation/BodyParts/Womb/Fluid/            womb fluid
      |- RJW_Menstruation_Fluids/BodyParts/Womb/Cumflated/ womb inflation
      |- RJW_Now_with_balls/BodyParts/{Testicles,Ovaries}/ gonads
      `- RJW_Panel/{Background,Anus}/         RJW part panel: per-body-type backgrounds and
                                              the anus window background
```

There is no `Patches/` folder. Nice Health Tab works through its own Def types rather than
PatchOperations, so this mod **only adds new Defs and patches no XML at all**.

### What the assembly does

**1. Automatic part index correction (once at startup)**
Nice Health Tab's doll Defs point at a part through an **integer index** into
`BodyDef.AllParts`, not through a defName. The indices of the parts RJW and the balls mod add
depend on the loaded mod set, so at startup the real indices are looked up and written into the
Defs. Mappings are also added to the index remaps of other races and to user overrides. When
the `Gonads` part is missing because the balls mod is absent, the gonad markers attach to the
genitals part and the form matching switches to the genital family.

**2. The plain size hediff filter (once at startup)**
Every `rjw.HediffDef_SexPart` that is neither `isBad` nor `countsAsAddedPartOrImplant` is added
to `HediffCache.IgnoreHediffs`, the set Nice Health Tab opened up for add-ons. They drop out of
the doll's condition and colour maths only, and remain in the list on the right.

**3. Per-pawn state (every time the health tab is drawn)**
A **single prefix** on vanilla `HealthCardUtility.DrawPawnHealthCard` (`Priority.First`,
returning void, so it does not interfere with the original flow). It reads the hediffs on each
part to pick the form, art and position, and hides what the settings say to hide (child body
type, male chest, futa testicles) by setting `bodyPartId` to -1 so the renderer skips it
quietly. The nipple colour, the womb fluid colour and the anus window are settled here too.

**4. The layers we draw ourselves (every time the doll is drawn)**
Layers whose colour comes from somewhere other than the health status - the **nipples** (nipple
colour), the **womb fluid** (fluid colour) and the **anus window** - are drawn by a prefix on
NHT's `DrawPart`. Every other part is drawn by NHT as usual.

**5. The Cumpilation compatibility guard (only with that mod)**
Pawns whose genitals are missing produce a `Tried to add health diff to missing part` error,
because Cumpilation's `CumflationUtility.GetOrCreateCumflationHediff` **creates and attaches** a
cumflation hediff to the pawn's genitals when there is none. Two places call it, so it fires in
two situations: when a surgery menu is opened (`Recipe_ExtractCum.AvailableOnNow`) and when a
pawn picks its next job (`ThinkNode_ConditionalCumflationSeverity.Satisfied`). The latter piles
up just from leaving the game running. It reproduces without this mod, but since this patch adds
one more thing to click on the doll, it is blocked here.

Rather than guarding each caller, the guard sits at **the one cause**. Only when the error would
really happen do we skip the original and return the same severity-0 hediff it would have made -
we only leave out the attaching. The original also returns the same object after its add fails,
so **behaviour is unchanged and only the error disappears**.

**6. Harvesting split into testicles / ovaries (only with the balls mod)**
The balls mod's harvesting surgery is a single recipe, so **you cannot choose what gets
removed**. Whichever row you click, natural testicles go first and the ovaries only when there
are none. There is no way to harvest ovaries alone while testicles are present, and a pawn with
artificial testicles plus ovaries is offered "neuter (archotech testicles)" while the ovaries are
what actually come out. In the doll menu the same row can even appear twice, because the recipe
is registered twice.

So this mod ships **remove testicles** and **remove ovaries** and attaches those to whichever
races had the original harvesting recipe. The original is only dropped from the lists - its Def
stays, so a bill queued in an older save still finishes. The removal itself (the organ item, the
original owner's details, the Neutered hediff, no failure) matches the balls mod. Artificial
testicles and ovaries are not harvestable, exactly as on their side. With the balls mod present,
the gonad part names are also filled in from this mod's translation.

**7. The RJW part panel (every time the health tab is drawn)**
Hooks on NHT `DollWidget`'s `DrawMainDoll` (a prefix that remembers the doll column's rect) and
`DrawOverlay` (a postfix that draws the button and the panel). The button's crotch picture uses
NHT's `DollRenderer.DrawDoll` to draw the main doll large and clips everything outside the
button. While that runs, organs, the anus and the head are skipped, and NHT's scar list and tend
information are swapped out and back so scars and bandages cannot spill outside the frame.

Shared rules

- The Nice Health Tab / RimJobWorld / balls / Menstruation / Cumpilation assemblies are
  **never hard-referenced** (everything is reflection). With any of them missing, nothing
  happens and there is no `TypeLoadException`.
- Harmony is used for **prefixes and postfixes only**; there are no transpilers.

  | Target | Kind | What it does |
  |---|---|---|
  | `HealthCardUtility.DrawPawnHealthCard` | prefix | per-pawn form, size and visibility (returns void) |
  | NHT `DollPartRenderer.DrawPart` / `DollDrawer.DrawPart` | prefix | draws the nipples, womb fluid and anus window; filters parts inside the panel button |
  | NHT `DollWidget.DrawMainDoll` / `DrawOverlay` | prefix / postfix | the RJW part panel and its button |
  | `Cumpilation.Cumflation.CumflationUtility.GetOrCreateCumflationHediff` | prefix | stops a hediff being attached to missing genitals |
  | balls `AutoOrganAdder.AddRecipesToAnimals` | postfix | swaps the original harvesting recipe for remove testicles / remove ovaries |

  `0Harmony.dll` is not redistributed - the Harmony mod provides it.
- On failure a single `Log.Message` is written and only that feature is dropped. No red errors.

Rebuild:

```bash
cd Source && dotnet build -c Release
```

If RimWorld is installed somewhere else:

```bash
dotnet build -c Release -p:RimWorldManaged="D:\Games\RimWorld\RimWorldWin64_Data\Managed"
```

---

## Credits

- **Nice Health Tab** - Andromeda ([Steam 3328729902](https://steamcommunity.com/sharedfiles/filedetails/?id=3328729902))
- **RimJobWorld** - Ed86 (<https://gitgud.io/Ed86/rjw>)
- **RJW Now with balls! . . . and Ovaries I guess** - TeheeItsMe
- **RJW Menstruation Cycle** - lutepickle
- **RJW Menstruation - Fluids** - eltoro, alpenglow (<https://gitgud.io/ElToro/rjw-menstruation-fluids>)
- **Sized Apparel for RJW** - OTYOTY
- **Cumpilation** - Vegapnk (1.6 rewrite by Telanda)
- **RimJobWorld - Licentia Serums** - LustLicentia, Linkolas, Ed86, John the Anabaptist, Ryufais

This mod is an independent compatibility patch. It neither contains nor modifies any of the
works above.
