using System;
using System.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace CloudQAFormAutomation
{
    /// <summary>
    /// Automates four fields on the CloudQA Automation Practice Form:
    ///   1. First Name  - a plain text input
    ///   2. Gender      - a radio-button group
    ///   3. Country     - a &lt;select&gt; dropdown (or an autocomplete widget,
    ///                    depending on the page build - handled either way)
    ///   4. State       - a native &lt;select&gt; dropdown (bonus field, added
    ///                    to prove the same locator/select-handling code
    ///                    works cleanly on a second, structurally distinct
    ///                    native dropdown)
    ///
    /// Every element is found through <see cref="ResilientElementLocator"/>,
    /// which locates fields by their visible label / option text rather than
    /// by id, name, class, XPath position or DOM order - see that file, and
    /// the README, for the full explanation of the strategy.
    /// </summary>
    public class Program
    {
        private const string FormUrl = "https://app.cloudqa.io/home/AutomationPracticeForm";

        public static void Main(string[] args)
        {
            var options = new ChromeOptions();
            // Comment out the next line to watch the browser drive the form live.
            if (args.Length > 0 && args[0] == "--headless")
            {
                options.AddArgument("--headless=new");
            }
            options.AddArgument("--window-size=1400,1000");

            IWebDriver driver = new ChromeDriver(options);
            var results = new TestResultCollector();

            try
            {
                driver.Navigate().GoToUrl(FormUrl);
                new WebDriverWait(driver, TimeSpan.FromSeconds(10))
                    .Until(d => ((IJavaScriptExecutor)d)
                        .ExecuteScript("return document.readyState")!.Equals("complete"));

                var locator = new ResilientElementLocator(driver);

                RunFirstNameTest(locator, results);
                RunGenderTest(driver, locator, results);
                RunCountryTest(driver, locator, results);
                RunStateTest(locator, results);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FATAL: unhandled error during test run: {ex.Message}");
            }
            finally
            {
                results.PrintSummary();
                driver.Quit();
            }
        }

        // ---------------------------------------------------------------
        // Field 1: First Name (text input)
        // ---------------------------------------------------------------
        private static void RunFirstNameTest(ResilientElementLocator locator, TestResultCollector results)
        {
            const string testName = "First Name (text input)";
            try
            {
                IWebElement firstName = locator.FindByLabel("First Name", preferredTag: "input");

                const string valueToEnter = "Rohit";
                firstName.Clear();
                firstName.SendKeys(valueToEnter);

                string actual = firstName.GetAttribute("value") ?? string.Empty;
                bool passed = actual == valueToEnter;

                results.Record(testName, passed,
                    passed
                        ? $"Entered '{valueToEnter}' and confirmed the field's value attribute matches."
                        : $"Expected '{valueToEnter}' but field held '{actual}'.");
            }
            catch (Exception ex)
            {
                results.Record(testName, false, $"Locator/interaction failed: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------
        // Field 2: Gender (radio group)
        // ---------------------------------------------------------------
        private static void RunGenderTest(IWebDriver driver, ResilientElementLocator locator, TestResultCollector results)
        {
            const string testName = "Gender (radio button)";
            try
            {
                IWebElement maleRadio = locator.FindChoiceInputByText("Male");

                if (!maleRadio.Selected)
                {
                    // Some frameworks intercept a plain .Click() on a styled radio
                    // (e.g. a custom overlay); JS-click is a reliability fallback
                    // for the interaction step only - it has nothing to do with
                    // how the element was located above.
                    try { maleRadio.Click(); }
                    catch (ElementClickInterceptedException)
                    {
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", maleRadio);
                    }
                }

                bool passed = maleRadio.Selected;
                results.Record(testName, passed,
                    passed
                        ? "Selected the 'Male' option and confirmed it is checked."
                        : "Clicked 'Male' but the radio button did not register as selected.");
            }
            catch (Exception ex)
            {
                results.Record(testName, false, $"Locator/interaction failed: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------
        // Field 3: Country (rendered as either a native <select> or an
        // autocomplete/combobox <input> depending on the page build - the
        // locator finds the field by its "Country" label either way; only
        // the *interaction* branches on which one it actually got back.
        // ---------------------------------------------------------------
        private static void RunCountryTest(IWebDriver driver, ResilientElementLocator locator, TestResultCollector results)
        {
            const string testName = "Country (dropdown)";
            const string desiredCountry = "India";

            try
            {
                // No preferredTag filter here on purpose: we want whichever
                // element the "Country" label actually points to, native
                // <select> or autocomplete <input>, and decide what to do
                // with it only after we see its real tag.
                IWebElement countryField = locator.FindByLabel("Country");
                string tagName = countryField.TagName.ToLowerInvariant();

                bool passed;
                string detail;

                if (tagName == "select")
                {
                    var select = new SelectElement(countryField);
                    select.SelectByText(desiredCountry);
                    string actual = select.SelectedOption.Text.Trim();
                    passed = actual == desiredCountry;
                    detail = passed
                        ? $"Selected '{desiredCountry}' from the native <select> and confirmed it is now chosen."
                        : $"Expected '{desiredCountry}' selected but found '{actual}'.";
                }
                else
                {
                    // This field is CloudQA's own hand-rolled autocomplete widget
                    // (visible in the page's <script> block), not a generic
                    // combobox. When you type, it injects suggestion rows into
                    // a <div class="autocomplete-items">, and - this is the key
                    // stable signal - each row also contains a hidden
                    // <input type="hidden" value="India"> carrying the exact
                    // country string. That hidden value is content the widget
                    // itself guarantees will be correct and present; it's not an
                    // id/class a developer chose and could rename, so it's a
                    // safer anchor than guessing at row markup.
                    countryField.Click();
                    countryField.Clear();
                    countryField.SendKeys(desiredCountry);

                    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));
                    IWebElement hiddenValueHolder = wait.Until(d =>
                    {
                        var candidates = d.FindElements(By.XPath(
                                "//div[contains(concat(' ',normalize-space(@class),' '),' autocomplete-items ')]" +
                                "//input[@type='hidden']" +
                                "[translate(@value,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')" +
                                $"='{desiredCountry.ToLowerInvariant()}']"))
                            .ToList();
                        return candidates.Count > 0 ? candidates[0] : null;
                    });

                    // Click the visible row wrapping the hidden input (that's
                    // what the widget's own click handler listens on).
                    IWebElement suggestionRow = hiddenValueHolder.FindElement(By.XPath(".."));
                    suggestionRow.Click();

                    string actual = (countryField.GetAttribute("value") ?? string.Empty).Trim();
                    passed = actual.Equals(desiredCountry, StringComparison.OrdinalIgnoreCase);
                    detail = passed
                        ? $"Typed '{desiredCountry}' into the autocomplete field, clicked the matching suggestion (found via its hidden value-holding input), and confirmed the field now shows it."
                        : $"Typed '{desiredCountry}' but the field ended up showing '{actual}'.";
                }

                results.Record(testName, passed, detail);
            }
            catch (Exception ex)
            {
                results.Record(testName, false, $"Locator/interaction failed: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------
        // Field 4 (bonus): State - a native <select> dropdown, structurally
        // distinct from Country's widget. Rather than hard-coding an
        // expected option text (this page's "State" list is a quirky mix
        // of country-like names, not real US/India states), the test picks
        // the first real option (index 1, skipping the usual blank
        // placeholder at index 0), selects it, and confirms the select's
        // chosen value now matches that option's own text. That keeps the
        // test content-based rather than tied to a specific label value
        // that could differ per environment.
        // ---------------------------------------------------------------
        private static void RunStateTest(ResilientElementLocator locator, TestResultCollector results)
        {
            const string testName = "State (native dropdown, bonus)";
            try
            {
                IWebElement stateField = locator.FindByLabel("State", preferredTag: "select");
                var select = new SelectElement(stateField);

                var options = select.Options.Where(o => o.Enabled).ToList();
                // Skip a leading blank/placeholder option ("-- Select --" etc.) if present.
                int indexToPick = (options.Count > 1 && string.IsNullOrWhiteSpace(options[0].Text)) ? 1 : 0;
                string expected = options[indexToPick].Text.Trim();

                select.SelectByText(expected);
                string actual = select.SelectedOption.Text.Trim();
                bool passed = actual == expected;

                results.Record(testName, passed,
                    passed
                        ? $"Selected '{expected}' from the native State <select> and confirmed it is now chosen."
                        : $"Expected '{expected}' selected but found '{actual}'.");
            }
            catch (Exception ex)
            {
                results.Record(testName, false, $"Locator/interaction failed: {ex.Message}");
            }
        }
    }

    /// <summary>Small helper to collect pass/fail results and print a readable summary.</summary>
    internal class TestResultCollector
    {
        private readonly System.Collections.Generic.List<(string Name, bool Passed, string Detail)> _results = new();

        public void Record(string name, bool passed, string detail)
        {
            _results.Add((name, passed, detail));
            Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}: {detail}");
        }

        public void PrintSummary()
        {
            Console.WriteLine();
            Console.WriteLine("==================== SUMMARY ====================");
            foreach (var (name, passed, _) in _results)
            {
                Console.WriteLine($"{(passed ? "PASS" : "FAIL"),-6} {name}");
            }
            int passCount = _results.FindAll(r => r.Passed).Count;
            Console.WriteLine($"{passCount}/{_results.Count} field tests passed.");
            Console.WriteLine("==================================================");
        }
    }
}
