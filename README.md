# CloudQA Automation Practice Form — Resilient Selenium Automation (C#)

Automates four fields on the [CloudQA Automation Practice Form](https://app.cloudqa.io/home/AutomationPracticeForm), plus a bonus live resilience demo.

| # | Field      | Type                          | Action |
|---|------------|-------------------------------|--------|
| 1 | First Name | text input                    | Enter text, validate it stuck |
| 2 | Gender     | radio buttons                 | Select "Male", validate it's checked |
| 3 | Country    | custom autocomplete (no real `<select>`) | Type "India", click matching suggestion, validate |
| 4 | State      | native `<select>`             | Select first real option, validate it's chosen |
| 5 | *(bonus)*  | —                              | Mutate a field's id/name/class + move it in the DOM at runtime, confirm locator still finds it |

The task asked for 3 fields — 4 are automated here (State added to prove the same locator handles a second, structurally different widget type), plus the optional bonus.

The focus isn't the interactions — it's **how elements are found**. See below.

---

## Project Structure

```
CloudQAFormAutomation/
├── CloudQAFormAutomation.csproj
├── Program.cs                    # entry point, drives all 5 tests
├── ResilientElementLocator.cs    # the locator engine (core of the task)
└── README.md
```

## Setup

**Prerequisites**
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Google Chrome installed (any recent version — Selenium Manager auto-downloads a matching `chromedriver`, no manual setup needed)

**Install dependencies**
```bash
cd CloudQAFormAutomation
dotnet restore
```
Pulls in `Selenium.WebDriver` (core bindings + Selenium Manager), `Selenium.Support` (`SelectElement`, `WebDriverWait`), `DotNetSeleniumExtras.WaitHelpers`.

## Running

```bash
dotnet run
```
Opens Chrome, navigates to the form, runs all 5 tests, prints PASS/FAIL + summary.

Headless (e.g. CI):
```bash
dotnet run -- --headless
```

**Sample output**
```
[PASS] First Name (text input): Entered 'Rohit' and confirmed the field's value attribute matches.
[PASS] Gender (radio button): Selected the 'Male' option and confirmed it is checked.
[PASS] Country (dropdown): Typed 'India', clicked the matching suggestion, confirmed the field shows it.
[PASS] State (native dropdown, bonus): Selected 'Afganistan' and confirmed it is chosen.
[PASS] BONUS: locator survives live id/class/DOM-position mutation.

5/5 field tests passed.
```
- Console also prints a `[locator] ...` line per field showing which strategy resolved it — useful for debugging and for seeing the resilience in action.
- Chrome/GCM diagnostic noise (`update_service_dialer_win.cc`, etc.) may appear — that's Chrome itself, not this project, and doesn't affect results.
- For the bonus test specifically, notice **two** `[locator]` lines: the first call resolves via the strong `label[for]` strategy; the second (post-mutation) call falls through to a weaker fallback (`label containing text → nearest following input`) — exactly the expected behavior once id/DOM structure is destroyed.

---

## Locator Strategy

**The problem with typical Selenium code:**
```csharp
driver.FindElement(By.Id("firstName"));           // breaks when the id changes
driver.FindElement(By.XPath("//div[3]/input[1]")); // breaks when layout changes
```
Both depend on implementation details a developer can change without the user noticing.

**The approach here:** `ResilientElementLocator` never hard-codes a single selector. For each field it tries a ranked list of independent, content-based strategies, stopping at the first that resolves:

1. `<label for="...">` → id lookup — the "correct" HTML association; also what screen readers rely on, so redesigns rarely break it
2. `<label>` that visually wraps the control — for forms that skip `for`/`id`
3. Label text `contains()` match → nearest following control — tolerates extra whitespace, asterisks, tooltips
4. `placeholder` attribute match
5. `aria-label` / accessible-name match
6. Any element with matching visible text → nearest input/select/textarea (broadest fallback)

Radio/checkboxes use the same idea on the *option* text ("Male"): `label[for]` → wrapping label → adjacent text → `value` attribute, in that order.

A strategy that times out is silently skipped; the next one is tried. Calling code never needs to know which one worked.

**Handling Country's non-standard widget:** it's a hand-rolled JS autocomplete, not a real `<select>` — no `<option>`s, no standard suggestion markup (`<li>`, `role="option"`) to key off. So after locating the field itself via the normal label strategies, the code waits for a suggestion row identifiable by content: each row contains a hidden `<input type="hidden" value="India">` — a value the widget itself guarantees, unlike an id/class a developer chose.

---

## How This Handles Page/HTML Changes

| Change | How it's handled |
|---|---|
| Position/order changes | No strategy uses index or absolute path — all are content-based |
| id/name/class change | No strategy keys off these; strategy 1 *reads* `@for`/`@id` at runtime rather than assuming a fixed value |
| DOM structure changes slightly | `ancestor::`/`following::` axes used instead of fixed parent-child chains |
| XPath/CSS path changes | No single path per field — 4–6 independent ones, so one breaking rarely breaks all |
| Similar attributes across elements | Every strategy narrows by field-specific text *before* touching DOM relationships |

---

## Bonus: Element ID When Most Attributes Change

Demonstrated live via `RunLocatorResilienceDemo` (5th test, separate from the form tests):

1. Locates First Name normally, types a unique fingerprint value into it
2. Runs JS that:
   - Removes the *original* `<label>` (captured via the input's `.labels` before anything changes) — so it can't produce a stale match later
   - Randomizes `id`, `name`, `class` to garbage
   - Moves the input into a brand-new `<div>` at the end of `<body>`, with a freshly-created `<label>` (same text) placed right before it
3. Calls `FindByLabel("First Name")` again — same call as every other test — and confirms it's the *same* element via the fingerprint

Why removing the original label matters: without it, the weaker fallback strategy could match the stale leftover label and walk to the wrong input — a false pass. Only one "First Name" label exists post-mutation, so a real pass here proves the locator followed the live content, not leftover markup.

This works because each strategy needs only *one* surviving signal — here, a label with matching text near the field. If literally every signal (including label text) were destroyed, no purely static approach would work without fuzzy/semantic matching — see Limitations.

---

## Assumptions & Limitations

- **Chrome only** — wired to `ChromeDriver`; `ResilientElementLocator` itself works unmodified with Firefox/Edge if the driver instantiation is swapped
- **Label text is the trusted anchor** — assumes visible text is the least likely thing to change (breaking it would confuse real users too). A wording change (e.g. "First Name" → "Given Name") would defeat this, same as any locator; a fuzzy/near-match extension (e.g. Levenshtein distance) would be the natural next step
- **First visible match wins** if a strategy matches more than one element; count is logged for visibility
- **State's options are data-dependent** — the live page's "State" dropdown actually lists country names (a quirk of this practice page), including a misspelling ("Afganistan"). Rather than hard-coding an expected value, the test reads whatever the first real option actually is (skipping an obvious placeholder like `-- Select Country --`)
- **No CAPTCHA/auth handling** — not needed for this form
- **Short timeouts** (5s per strategy, 10s initial load) to keep the run fast — increase `timeoutSeconds` in `ResilientElementLocator`'s constructor for slower environments/CI
