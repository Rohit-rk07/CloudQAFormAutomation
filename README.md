# CloudQA Automation Practice Form — Resilient Selenium Automation (C#)

Automates four fields on the [CloudQA Automation Practice Form](https://app.cloudqa.io/home/AutomationPracticeForm),
plus a fifth, separate demonstration for the assessment's optional bonus section:

| # | Field      | Type                                  | Action performed                                                        |
|---|------------|----------------------------------------|--------------------------------------------------------------------------|
| 1 | First Name | text input                             | Enter text, then validate the value stuck                                |
| 2 | Gender     | radio buttons                          | Select "Male", then validate it's checked                                |
| 3 | Country    | custom autocomplete widget (not a real `<select>`) | Type "India", click the matching suggestion, validate the field shows it |
| 4 | State      | native `<select>` dropdown             | Select the first real option, validate it's chosen                       |
| 5 | *(bonus)*  | — | Live demo: mutate a field's id/name/class and move it elsewhere in the DOM at runtime, then confirm the locator still finds it |

The task only asks for three fields; I automated four (a fourth field, State, was
added to show the same locator code handling a second, structurally different
widget type) plus the optional bonus demo.

The focus of this assessment isn't the interactions themselves — it's
**how the elements are found**. See [Locator Strategy](#locator-strategy--resilience) below.

---

## Project Structure

```
CloudQAFormAutomation/
├── CloudQAFormAutomation.csproj      # project + NuGet dependencies
├── Program.cs                        # entry point; drives all 5 tests
├── ResilientElementLocator.cs        # the resilient locator engine (core of the task)
└── README.md
```

## Setup

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (LTS)
- Google Chrome installed locally (any recent version — Selenium's
  built-in Selenium Manager, bundled with `Selenium.WebDriver` 4.6+,
  auto-detects the installed Chrome version and downloads a matching
  `chromedriver` binary at run time, so no manual driver download or
  version pinning is needed)

### Install dependencies

```bash
cd CloudQAFormAutomation
dotnet restore
```

This pulls in:
- `Selenium.WebDriver` — the core Selenium .NET bindings, including
  Selenium Manager (auto-resolves the right `chromedriver` for whatever
  Chrome version is installed — no separate driver package needed)
- `Selenium.Support` — `SelectElement`, `WebDriverWait`, etc.
- `DotNetSeleniumExtras.WaitHelpers` — small wait-condition helpers

## Running the automation

```bash
dotnet run
```

This opens a visible Chrome window, navigates to the form, and runs all
five tests, printing a PASS/FAIL line per test plus a summary.

To run headless (e.g. in CI):

```bash
dotnet run -- --headless
```

### Example output

```
  [locator] 'field labelled 'First Name'' resolved via: label[for] pointing at a input (label text = 'First Name')
[PASS] First Name (text input): Entered 'Rohit' and confirmed the field's value attribute matches.
  [locator] 'choice input for option 'Male'' resolved via: radio/checkbox immediately followed by text 'Male'  (took first of 2 matches)
[PASS] Gender (radio button): Selected the 'Male' option and confirmed it is checked.
  [locator] 'field labelled 'Country'' resolved via: label[for] pointing at a * (label text = 'Country')
[PASS] Country (dropdown): Typed 'India' into the autocomplete field, clicked the matching suggestion (found via its hidden value-holding input), and confirmed the field now shows it.
  [locator] 'field labelled 'State'' resolved via: label[for] pointing at a select (label text = 'State')
[PASS] State (native dropdown, bonus): Selected 'Afganistan' from the native State <select> and confirmed it is now chosen.
  [locator] 'field labelled 'First Name'' resolved via: label[for] pointing at a input (label text = 'First Name')
  [locator] 'field labelled 'First Name'' resolved via: label containing text, then nearest following input
[PASS] BONUS: locator survives live id/class/DOM-position mutation: After removing the original label, randomizing id/name/class (new id 'xmnmgshrn'), and moving the element into a brand-new <div> at the end of <body>, FindByLabel('First Name') still resolved to the SAME element (fingerprint confirmed intact).

==================== SUMMARY ====================
PASS   First Name (text input)
PASS   Gender (radio button)
PASS   Country (dropdown)
PASS   State (native dropdown, bonus)
PASS   BONUS: locator survives live id/class/DOM-position mutation
5/5 field tests passed.
==================================================
```

(Chrome/Selenium/GCM diagnostic noise from Chrome itself — e.g.
`update_service_dialer_win.cc`, `registration_request.cc` lines — may also
appear in the console; these come from the Chrome binary, not this project,
and don't affect the test results.)

The `[locator]` lines are printed for every field so you can see, at
runtime, exactly which fallback strategy resolved each element — useful
both for debugging and for demonstrating the resilience behaviour below.
Notice the second `[locator]` line for the bonus test: the *first* call
resolves normally (`label[for]`), and the *second* call — made only after
the DOM has been mutated — falls through to a weaker fallback strategy
(`label containing text, then nearest following input`), which is exactly
the expected, correct behaviour: the strong strategies should fail once
the id/DOM structure is destroyed, and a weaker-but-still-content-based
strategy should pick up the slack.

---

## Locator Strategy & Resilience

### The problem with typical Selenium code
Most Selenium scripts locate elements like this:

```csharp
driver.FindElement(By.Id("firstName"));          // breaks the moment the id changes
driver.FindElement(By.XPath("//div[3]/input[1]")); // breaks if the layout changes at all
```

Both are brittle: they depend on implementation details (an id string, a
DOM position) that a front-end developer can — and routinely does — change
without touching what the *user* sees.

### The approach used here
`ResilientElementLocator` never hard-codes a single selector for a field.
Instead, for each field it builds a **ranked list of independent
strategies**, all derived from information a real user (or a screen
reader) relies on to identify the field — not from implementation details:

1. **`<label for="...">` → id lookup** — the semantically "correct" HTML
   association. This is also what assistive technology depends on, so it's
   the attribute a redesign is *least* likely to break, since breaking it
   breaks accessibility too.
2. **`<label>` that visually wraps the control** — covers forms that skip
   the `for`/`id` pairing and nest the input inside the label instead.
3. **Label text `contains()` match, then nearest following control** —
   handles extra whitespace, a required-field `*`, or a tooltip appended
   to the label.
4. **`placeholder` attribute match** — for inputs that use placeholder
   text instead of a visible label.
5. **`aria-label` / accessible-name match** — for icon-only or visually
   label-less controls.
6. **Any element with matching visible text → nearest input/select/
   textarea** — the broadest, most tolerant fallback.

For radio buttons/checkboxes, the same idea is applied to the *option*
text ("Male") instead of a field label: `label[for]` → wrapping label →
adjacent text node → `value` attribute, in that order.

`FindByLabel()` / `FindChoiceInputByText()` walk this list top to bottom
and **stop at the first strategy that resolves to a real, visible
element**. If a strategy times out (element not found that way), it's
silently skipped and the next one is tried — the calling code never needs
to know which strategy actually worked.

### Handling a non-standard widget: Country
The Country field isn't a native `<select>` on this page — it's a
hand-rolled JavaScript autocomplete. There are no `<option>` elements to
select from, and suggestion rows have no standardized markup (`<li>`,
`role="option"`, etc. don't exist here) to key off. Rather than guessing
at row structure, the code locates the field itself via the normal label
strategies (which don't care what tag the field turns out to be), types
into it, and then waits for a suggestion row identifiable by content: each
row contains a hidden `<input type="hidden" value="India">` carrying the
real value — content the widget itself guarantees, unlike an id or class
a developer happened to choose.

### Why this satisfies each resilience requirement

| Requirement in the task                          | How it's handled |
|----------------------------------------------------|-------------------|
| Element position/order changes                     | No XPath here encodes a numeric index or absolute path — every predicate is content-based (`text()`, `@placeholder`, `@aria-label`), so reordering DOM nodes doesn't affect matching. |
| `id`/`name`/`class` change                          | None of the 6 strategies key off `id`, `name`, or `class` at all — strategy 1 *reads* `@for`/`@id` at runtime rather than assuming a fixed value, so it still works even if those values are regenerated on every page load. |
| DOM structure changes slightly                      | `ancestor::`, `following::`, and `following-sibling::` axes are used instead of fixed parent/child chains, so an extra wrapper `<div>` or a re-nested layout doesn't break the match. |
| XPath/CSS path changes                              | There is no "the" XPath for a field — there are 4-6 independent ones, generated from different signals, so a change that defeats one rarely defeats them all. |
| Multiple elements share similar attributes          | Every strategy first narrows by *text content specific to that field* (its exact label or option text) before touching the DOM relationship, so a generic `class="form-control"` shared by ten inputs is never the deciding factor. |

### Bonus: identifying an element when most attributes have changed
This is demonstrated live, not just argued for. `Program.cs` runs a 5th,
clearly-separate test — `RunLocatorResilienceDemo` — that is **not** a test
of the CloudQA form's own behaviour. Instead it:

1. Locates the First Name field the normal way and types a unique
   fingerprint value into it.
2. Runs a JavaScript snippet that:
   - removes the *original* `<label>` (captured via the input's own
     `.labels` association before anything else changes), so it can't be
     matched again once the input has moved away from it;
   - randomizes the element's `id`, `name`, and `class` to garbage;
   - physically moves the input into a brand-new `<div>` appended at the
     end of `<body>` — destroying its attributes, its position, its DOM
     structure, and any XPath that encoded its old location — while
     planting a freshly-created `<label>` carrying the same label text
     immediately before it in its new location.

   This mirrors how a real redesign usually keeps the *visible copy* of a
   label next to its control even while completely rewriting the
   surrounding markup — and removing the stale original label matters:
   without that step, the locator's weaker fallback strategy could latch
   onto the leftover label instead and walk forward to the wrong,
   unrelated input, producing a false pass rather than a real one.
3. Calls `FindByLabel("First Name")` again — the exact same call every
   other test uses — and confirms it resolved to the **same element** by
   reading the fingerprint value (and the freshly-randomized id) back out.

This works because each of the six strategies depends on only a *single*
signal rather than a specific combination of id+class+position, so as
long as one signal (here, a label with matching text, adjacent in
document order) survives, one strategy still resolves it. If literally
every signal were destroyed at once — including the label text itself —
no purely static approach could succeed without fuzzy/semantic matching;
see Limitations.

---

## Assumptions & Limitations

- **Chrome only.** The project is wired to `ChromeDriver`; the same
  `ResilientElementLocator` class works unmodified with `FirefoxDriver` /
  `EdgeDriver` if you swap the driver instantiation in `Program.cs`.
- **Label text is treated as the stable anchor.** The strategy assumes the
  *visible, human-readable* text (label/placeholder/option text) is the
  part of the page least likely to change, since changing it would confuse
  real users too. If a redesign changes the wording itself (e.g. "First
  Name" → "Given Name"), no attribute-based locator — including this one —
  can find it without either fuzzy/semantic text matching or a config
  mapping of "old label → new label", which is out of scope here.
  A natural extension would be scoring near-matches (e.g. Levenshtein
  distance) instead of requiring an exact/contains match.
- **First visible match is used.** If a strategy's XPath matches more than
  one visible element (which the field-specific text narrowing is designed
  to avoid), the locator takes the first and logs how many it found, so
  it's easy to spot ambiguity.
- **The State field's option text is data-dependent.** On the live page,
  the "State" dropdown actually contains a list of country names (a quirk
  of this particular practice page, not something this project controls),
  including at least one misspelling ("Afganistan"). Rather than hard-code
  an expected value, the test reads whichever option text is actually
  first in the list (skipping an obvious placeholder such as
  `-- Select Country --`) and asserts against that, so it stays correct
  regardless of exactly what the options are.
- **No CAPTCHA/auth handling** — the practice form doesn't require login,
  so none is implemented.
- **Timeouts** are short (5s per strategy, 10s for initial page load) to
  keep the demo fast; increase `timeoutSeconds` in
  `ResilientElementLocator`'s constructor for slower environments/CI.
