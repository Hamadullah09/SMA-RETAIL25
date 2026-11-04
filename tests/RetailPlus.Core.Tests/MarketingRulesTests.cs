using RetailPlus.Core.Rules;

namespace RetailPlus.Core.Tests;

/// <summary>Segment rules, WhatsApp numbers and message templates.</summary>
public class MarketingRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);

    private static SegmentFacts Client(decimal spend = 0, int visits = 0, DateOnly? last = null, bool dept = false, DateOnly? birthday = null, decimal points = 0,
        decimal balance = 0, bool optIn = false, string? number = null) => new(spend, visits, last, dept, birthday, points, balance, optIn, number);

    [Fact]
    public void Spend_and_visits_need_both_ends_of_the_range()
    {
        var rules = new SegmentRules { MinSpend = 1000, MaxSpend = 5000, MinVisits = 2 };
        Assert.True(Segments.Matches(rules, Client(spend: 1200, visits: 2), Today));
        Assert.False(Segments.Matches(rules, Client(spend: 900, visits: 5), Today));
        Assert.False(Segments.Matches(rules, Client(spend: 6000, visits: 5), Today));
        Assert.False(Segments.Matches(rules, Client(spend: 1200, visits: 1), Today));
    }

    [Fact]
    public void Lapsed_clients_last_bought_before_a_day_or_never_and_recent_ones_since()
    {
        var lapsed = new SegmentRules { LastPurchaseBefore = new DateOnly(2026, 8, 1) };
        Assert.True(Segments.Matches(lapsed, Client(last: new DateOnly(2026, 7, 31)), Today));
        Assert.True(Segments.Matches(lapsed, Client(), Today));
        Assert.False(Segments.Matches(lapsed, Client(last: new DateOnly(2026, 8, 1)), Today));
        var idle = new SegmentRules { NoPurchaseForDays = 90 };
        Assert.True(Segments.Matches(idle, Client(last: Today.AddDays(-90)), Today));
        Assert.False(Segments.Matches(idle, Client(last: Today.AddDays(-89)), Today));
        var recent = new SegmentRules { LastPurchaseAfter = new DateOnly(2026, 10, 1) };
        Assert.True(Segments.Matches(recent, Client(last: new DateOnly(2026, 10, 1)), Today));
        Assert.False(Segments.Matches(recent, Client(), Today));
    }

    [Fact]
    public void Birthday_points_balance_department_and_consent()
    {
        Assert.True(Segments.Matches(new SegmentRules { BirthdayMonth = 11 }, Client(birthday: new DateOnly(1990, 11, 20)), Today));
        Assert.False(Segments.Matches(new SegmentRules { BirthdayMonth = 11 }, Client(), Today));
        Assert.True(Segments.Matches(new SegmentRules { MinPoints = 100, MaxPoints = 200 }, Client(points: 150), Today));
        Assert.False(Segments.Matches(new SegmentRules { MinBalance = 1 }, Client(balance: 0), Today));
        Assert.True(Segments.Matches(new SegmentRules { DepartmentId = Guid.NewGuid() }, Client(dept: true), Today));
        Assert.False(Segments.Matches(new SegmentRules { DepartmentId = Guid.NewGuid() }, Client(), Today));
        var consent = new SegmentRules { WhatsAppConsent = true };
        Assert.True(Segments.Matches(consent, Client(optIn: true, number: "+923001234567"), Today));
        Assert.False(Segments.Matches(consent, Client(optIn: true), Today));
        Assert.True(Segments.Matches(new SegmentRules { WhatsAppConsent = false }, Client(number: "+923001234567"), Today));
    }

    [Fact]
    public void The_period_is_the_last_days_or_the_dates_and_wrong_rules_are_refused()
    {
        Assert.Equal((Today.AddDays(-29), Today), new SegmentRules { PeriodDays = 30 }.Period(Today));
        Assert.Equal((new DateOnly(2026, 1, 1), (DateOnly?)null), new SegmentRules { From = new DateOnly(2026, 1, 1) }.Period(Today));
        Assert.NotNull(new SegmentRules { MinSpend = 10, MaxSpend = 5 }.Problem());
        Assert.NotNull(new SegmentRules { BirthdayMonth = 13 }.Problem());
        Assert.Null(new SegmentRules { PeriodDays = 90, MinSpend = 1000 }.Problem());
        Assert.Equal("spent at least 1,000 in the last 90 days, birthday in March, agreed to WhatsApp",
            new SegmentRules { PeriodDays = 90, MinSpend = 1000, BirthdayMonth = 3, WhatsAppConsent = true }.Describe());
        Assert.Equal("Every client", new SegmentRules().Describe());
    }

    [Theory]
    [InlineData("+92 300 1234567", "+923001234567")]
    [InlineData("0092-300-1234567", "+923001234567")]
    [InlineData("+1 (415) 555-0100", "+14155550100")]
    [InlineData("  ", null)]
    public void WhatsApp_numbers_are_kept_in_international_form(string number, string? expected) =>
        Assert.Equal(expected, WhatsAppNumbers.Normalize(number));

    [Theory]
    [InlineData("03001234567")]
    [InlineData("+92abc")]
    [InlineData("+123")]
    public void A_number_without_a_country_code_is_refused(string number)
    {
        Assert.Throws<FormatException>(() => WhatsAppNumbers.Normalize(number));
        Assert.False(WhatsAppNumbers.IsValid(number));
    }

    [Fact]
    public void A_template_body_becomes_parameters_in_order_and_the_text_the_client_reads()
    {
        const string body = "Hi {FirstName}, you have {Points} points at {Store}. {FirstName}, see you soon!";
        Assert.Equal(["FirstName", "Points", "Store"], WhatsAppTemplates.Placeholders(body));
        var values = new TemplateValues("Ayesha", "Khan", null, 120.5m, 0, "Gulberg", "1001");
        Assert.Equal(["Ayesha", "120.5", "Gulberg"], WhatsAppTemplates.Parameters(body, values));
        Assert.Equal("Hi Ayesha, you have 120.5 points at Gulberg. Ayesha, see you soon!", WhatsAppTemplates.Render(body, values));
        Assert.Equal("Hi {{1}}, you have {{2}} points at {{3}}. {{1}}, see you soon!", WhatsAppTemplates.Numbered(body));
        // A blank value is sent as "-", which WhatsApp accepts.
        Assert.Equal(["-"], WhatsAppTemplates.Parameters("Dear {LastName}", new TemplateValues("A", null, null, 0, 0, null, null)));
    }

    [Fact]
    public void Templates_are_checked_like_WhatsApp_checks_them()
    {
        Assert.Null(WhatsAppTemplates.Problem("points_reminder", "en_US", "Hi {Name}"));
        Assert.NotNull(WhatsAppTemplates.Problem("Points Reminder", "en", "Hi"));
        Assert.NotNull(WhatsAppTemplates.Problem("points", "english", "Hi"));
        Assert.Contains("{Discount}", WhatsAppTemplates.Problem("points", "en", "Hi {Discount}"));
    }
}
