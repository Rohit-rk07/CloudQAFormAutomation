# CloudQA Automation Practice Form — Resilient Selenium Automation (C#)

Automates three fields on the [CloudQA Automation Practice Form](https://app.cloudqa.io/home/AutomationPracticeForm):

| Field         | Type          | Action performed                              |
|---------------|---------------|------------------------------------------------|
| First Name    | text input    | Enter text, then validate the value stuck      |
| Gender        | radio buttons | Select "Male", then validate it's checked      |
| Country       | select dropdown | Select "India", then validate it's chosen    |

The focus of this assessment isn't the three interactions themselves — it's
**how the elements are found**. See [Locator Strategy](#locator-strategy--resilience) below.

---

## Project Structure

```
CloudQAFormAutomation/
├── CloudQAFormAutomation.csproj      # project + NuGet dependencies
├── Program.cs                        # entry point; drives the 3 field tests
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
three field tests, printing a PASS/FAIL line per field plus a summary.

To run headless (e.g. in CI):

```bash
dotnet run -- --headless
```

### Example output

```
  [locator] 'field labelled 'First Name'' resolved via: label[for] pointing at a input (label text = 'First Name')
[PASS] First Name (text input): Entered 'Rohit' and confirmed the field's value attribute matches.
  [locator] 'choice input for option 'Male'' resolved via: label[for] radio/checkbox for option 'Male'
[PASS] Gender (radio button): Selected the 'Male' option and confirmed it is checked.
  [locator] 'field labelled 'Country'' resolved via: label[for] pointing at a select (label text = 'Country')
[PASS] Country (dropdown): Selected 'India' and confirmed it is now the chosen option.

==================== SUMMARY ====================
PASS   First Name (text input)
PASS   Gender (radio button)
PASS   Country (dropdown)
3/3 field tests passed.
==================================================
```

The `[locator]` lines are printed for every field so you can see, at
runtime, exactly which fallback strategy resolved each element — useful
both for debugging and for demonstrating the resilience behaviour below.

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

### Why this satisfies each resilience requirement

| Requirement in the task                          | How it's handled |
|----------------------------------------------------|-------------------|
| Element position/order changes                     | No XPath here encodes a numeric index or absolute path — every predicate is content-based (`text()`, `@placeholder`, `@aria-label`), so reordering DOM nodes doesn't affect matching. |
| `id`/`name`/`class` change                          | None of the 6 strategies key off `id`, `name`, or `class` at all — strategy 1 *reads* `@for`/`@id` at runtime rather than assuming a fixed value, so it still works even if those values are regenerated on every page load. |
| DOM structure changes slightly                      | `ancestor::`, `following::`, and `following-sibling::` axes are used instead of fixed parent/child chains, so an extra wrapper `<div>` or a re-nested layout doesn't break the match. |
| XPath/CSS path changes                              | There is no "the" XPath for a field — there are 4-6 independent ones, generated from different signals, so a change that defeats one rarely defeats them all. |
| Multiple elements share similar attributes          | Every strategy first narrows by *text content specific to that field* (its exact label or option text) before touching the DOM relationship, so a generic `class="form-control"` shared by ten inputs is never the deciding factor. |

### Bonus: identifying an element when most attributes have changed
This is effectively the normal case for the locator, not a special mode.
As long as **one** semantic signal survives — the label text, the
placeholder, the aria-label, or just the fact that the field type is an
`<input>`/`<select>` sitting next to recognizable text — one of the six
strategies will still resolve it, because each strategy depends on only a
*single* signal rather than a specific combination of id+class+position.
If literally every one of those signals were replaced at once (e.g. the
label text itself changed language and no placeholder/aria-label exists),
no purely static approach can succeed without additional context — see
Limitations.

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
- **No CAPTCHA/auth handling** — the practice form doesn't require login,
  so none is implemented.
- **Timeouts** are short (5s per strategy, 10s for initial page load) to
  keep the demo fast; increase `timeoutSeconds` in
  `ResilientElementLocator`'s constructor for slower environments/CI.
