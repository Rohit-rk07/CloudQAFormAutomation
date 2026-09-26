using System;
using System.Collections.Generic;
using System.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace CloudQAFormAutomation
{
    /// <summary>
    /// Describes one candidate strategy for locating an element. Strategies are
    /// tried in the order they are registered; the first one that resolves to
    /// exactly one visible, interactable element wins. Every strategy is built
    /// from information that is unlikely to change together: a field's visible
    /// label / placeholder / nearby text / role, NOT its id, name, class or
    /// absolute position - those are exactly the attributes the task says may
    /// be rewritten at any time.
    /// </summary>
    public sealed class LocatorStrategy
    {
        public string Description { get; }
        public By By { get; }

        public LocatorStrategy(string description, By by)
        {
            Description = description;
            By = by;
        }
    }

    /// <summary>
    /// Resolves a logical "field" (e.g. "the First Name text box") to a live
    /// IWebElement by trying a ranked list of semantic strategies instead of a
    /// single hard-coded selector. If the page's ids/classes/DOM order/XPath
    /// change but the human-readable label, placeholder text, ARIA role or
    /// visible neighbouring text stays put (which is normally the only part of
    /// a form a redesign leaves untouched, since users still have to be able to
    /// read the form), this class still finds the right element.
    ///
    /// Design notes (why this is resilient):
    ///   1. No single strategy is trusted. Each call tries several independent
    ///      signals and stops at the first that uniquely resolves.
    ///   2. Strategies are ordered from "most structurally correct" (an actual
    ///      &lt;label for="..."&gt; association, which is what a screen reader
    ///      would use) down to "fuzzy but still content-based" (any element
    ///      whose visible text roughly matches the label, walking to the
    ///      nearest input/select/textarea in the DOM).
    ///   3. Every XPath used is built from //*[...] content predicates
    ///      (text(), @placeholder, @aria-label, @type, role) - never from a
    ///      fixed absolute path, a specific id value or a specific index -
    ///      so it keeps working if elements are reordered, wrapped in new
    ///      containers, or given brand-new id/class/name values.
    ///   4. A short WebDriverWait backs every attempt so a slightly slower
    ///      render (e.g. from an extra wrapper div) doesn't register as "not
    ///      found".
    ///   5. Every piece of label/option text is passed through
    ///      <see cref="XpLiteral"/> before being embedded in an XPath
    ///      expression, so a value containing a quote character can't produce
    ///      a malformed (or, in principle, injectable) XPath string.
    /// </summary>
    public class ResilientElementLocator
    {
        private readonly IWebDriver _driver;
        private readonly WebDriverWait _wait;
        public bool VerboseLogging { get; set; } = true;

        public ResilientElementLocator(IWebDriver driver, int timeoutSeconds = 5)
        {
            _driver = driver;
            _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
        }

        /// <summary>
        /// Finds a labelled form control (input/select/textarea) by its
        /// human-readable label text, trying multiple content-based strategies
        /// in order of reliability. Throws if none of them resolve.
        /// </summary>
        public IWebElement FindByLabel(string labelText, string? preferredTag = null)
        {
            string tag = preferredTag ?? "*";
            string normalized = labelText.Trim();
            string lit = XpLiteral(normalized);
            string litLower = XpLiteral(normalized.ToLowerInvariant());

            var strategies = new List<LocatorStrategy>
            {
                // 1. Proper <label for="id"> association -> jump to the id it points at.
                //    This is the "correct" HTML way to link a label to its control, and
                //    is what assistive tech relies on, so pages rarely break it even
                //    when they rewrite ids, because doing so would break accessibility too.
                new LocatorStrategy(
                    $"label[for] pointing at a {tag} (label text = '{normalized}')",
                    By.XPath($"//label[normalize-space(text())={lit}]" +
                              "/ancestor::*[self::form or self::div][1]" +
                              $"//*[@id=//label[normalize-space(text())={lit}]/@for]")),

                // 2. <label> that visually wraps the control (no `for` needed).
                new LocatorStrategy(
                    $"label wrapping a {tag} (label text = '{normalized}')",
                    By.XPath($"//label[normalize-space(text())={lit}]//{tag}")),

                // 3. Label text contains (not just equals) the target - guards against
                //    extra whitespace, required-field asterisks, tooltips, etc.
                new LocatorStrategy(
                    $"label containing text, then nearest following {tag}",
                    By.XPath($"//label[contains(normalize-space(.),{lit})]" +
                              $"/following::{tag}[1]")),

                // 4. Placeholder attribute match (common for text inputs with no visible label).
                new LocatorStrategy(
                    $"{tag}[@placeholder contains '{normalized}']",
                    By.XPath($"//{tag}[contains(translate(@placeholder," +
                              "'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')," +
                              $"{litLower})]")),

                // 5. aria-label / aria-labelledby text match (accessible-name based).
                new LocatorStrategy(
                    $"{tag}[@aria-label contains '{normalized}']",
                    By.XPath($"//{tag}[contains(translate(@aria-label," +
                              "'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')," +
                              $"{litLower})]")),

                // 6. Generic "any element with this visible text, then the nearest
                //    interactive descendant-or-sibling control" - the broadest net,
                //    used as a last resort before giving up.
                new LocatorStrategy(
                    $"any element with text '{normalized}', nearest input/select/textarea",
                    By.XPath($"//*[normalize-space(text())={lit}]" +
                              "/following::*[self::input or self::select or self::textarea][1]")),
            };

            return TryStrategies(strategies, $"field labelled '{normalized}'");
        }

        /// <summary>
        /// Finds a radio button / checkbox by the visible text of its option
        /// (e.g. "Male"), independent of its id/name/value attributes.
        /// </summary>
        public IWebElement FindChoiceInputByText(string optionText)
        {
            string normalized = optionText.Trim();
            string lit = XpLiteral(normalized);
            string litLower = XpLiteral(normalized.ToLowerInvariant());

            var strategies = new List<LocatorStrategy>
            {
                new LocatorStrategy(
                    $"label[for] radio/checkbox for option '{normalized}'",
                    By.XPath($"//label[normalize-space(text())={lit}]" +
                              $"/ancestor::*[self::form or self::div][1]" +
                              $"//input[@type='radio' or @type='checkbox']" +
                              $"[@id=//label[normalize-space(text())={lit}]/@for]")),

                new LocatorStrategy(
                    $"label wraps radio/checkbox for option '{normalized}'",
                    By.XPath($"//label[normalize-space(text())={lit}]" +
                              "//input[@type='radio' or @type='checkbox']")),

                new LocatorStrategy(
                    $"radio/checkbox immediately followed by text '{normalized}'",
                    By.XPath($"//input[@type='radio' or @type='checkbox']" +
                              $"[following-sibling::*[1][normalize-space(text())={lit}]" +
                              $" or following-sibling::text()[normalize-space(.)={lit}]" +
                              $" or normalize-space(following::text()[1])={lit}]")),

                new LocatorStrategy(
                    $"radio/checkbox whose value attribute matches '{normalized}'",
                    By.XPath("//input[@type='radio' or @type='checkbox']" +
                              $"[translate(@value,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')" +
                              $"={litLower}]")),
            };

            return TryStrategies(strategies, $"choice input for option '{normalized}'");
        }

        private IWebElement TryStrategies(List<LocatorStrategy> strategies, string fieldDescription)
        {
            var attemptLog = new List<string>();

            foreach (var strategy in strategies)
            {
                try
                {
                    var elements = _wait.Until(drv =>
                    {
                        var found = drv.FindElements(strategy.By)
                                        .Where(e => e.Displayed)
                                        .ToList();
                        return found.Count > 0 ? found : null;
                    });

                    if (elements != null && elements.Count >= 1)
                    {
                        if (VerboseLogging)
                        {
                            Console.WriteLine($"  [locator] '{fieldDescription}' resolved via: {strategy.Description}" +
                                               (elements.Count > 1 ? $"  (took first of {elements.Count} matches)" : ""));
                        }
                        return elements[0];
                    }
                }
                catch (WebDriverTimeoutException)
                {
                    attemptLog.Add(strategy.Description);
                    // Not found with this strategy - fall through and try the next one.
                }
            }

            throw new NoSuchElementException(
                $"Could not resolve '{fieldDescription}' using any of {strategies.Count} fallback strategies. " +
                $"Attempted: {string.Join(" | ", strategies.Select(s => s.Description))}");
        }

        /// <summary>
        /// Builds a safe XPath 1.0 string literal for <paramref name="value"/>. A plain
        /// <c>'...'</c> or <c>"..."</c> wrap breaks as soon as the value contains that same
        /// quote character, and XPath 1.0 has no in-string escape sequence for it. This
        /// handles all three cases:
        ///   - No apostrophe in the value -&gt; wrap in single quotes: <c>'value'</c>.
        ///   - Apostrophe but no double-quote -&gt; wrap in double quotes: <c>"value"</c>.
        ///   - Both quote characters present (e.g. <c>O'Brien's "special" plan</c>) -&gt;
        ///     split the value on apostrophes (each resulting chunk is then guaranteed
        ///     apostrophe-free, so it's always safe to wrap it in single quotes even if
        ///     it still contains a double quote) and rebuild it with <c>concat()</c>,
        ///     splicing in a literal apostrophe - written as the double-quoted string
        ///     <c>"'"</c> - between chunks.
        /// Returns a complete literal/expression (e.g. <c>'value'</c> or
        /// <c>concat('a', "'", 'b')</c>) - callers embed the result directly and must NOT
        /// wrap it in another pair of quotes.
        /// </summary>
        private static string XpLiteral(string value)
        {
            if (!value.Contains('\''))
            {
                return $"'{value}'";
            }
            if (!value.Contains('"'))
            {
                return $"\"{value}\"";
            }

            // Both ' and " are present: build concat('chunk', "'", 'chunk', ...).
            string[] chunks = value.Split('\'');
            var parts = new List<string>();
            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunks[i].Length > 0)
                {
                    parts.Add($"'{chunks[i]}'");
                }
                if (i < chunks.Length - 1)
                {
                    parts.Add("\"'\"");
                }
            }
            return $"concat({string.Join(", ", parts)})";
        }
    }
}
