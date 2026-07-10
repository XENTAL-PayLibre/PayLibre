using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Authentication;
using PayLibre.Application.Common;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Enrolment;
using PayLibre.Infrastructure.Persistence;
using PayLibre.Tests.TestSupport;

namespace PayLibre.Tests;

public class EnrolmentServiceTests
{
    private static readonly IOptions<PayLibreOptions> Opts = Options.Create(new PayLibreOptions());

    private static async Task<Guid> SeedSchoolAsync(TestDb db, FakeXentalClient xental)
    {
        await using var ctx = db.CreateContext();
        var auth = new AuthService(ctx, new FakePasswordHasher(), new FakeTokenService(), xental, new FakeNotificationSender(), db.Clock, Opts);
        var s = await auth.RegisterAsync(new RegisterSchoolInput(
            "Acme Academy", "owner@acme.edu", "08012345678", "Test Bank", "999", "0123456789", "password1"));
        db.Tenant.TenantId = s.School.Id;
        return s.School.Id;
    }

    private static ClassService Classes(PayLibreDbContext ctx, TestDb db) => new(ctx, db.Tenant);
    private static StudentService Students(PayLibreDbContext ctx, TestDb db, FakeXentalClient x, FakeNotificationSender n) =>
        new(ctx, db.Tenant, x, n, Opts);

    [Fact]
    public async Task Create_class_then_reject_a_duplicate()
    {
        using var db = new TestDb();
        await SeedSchoolAsync(db, new FakeXentalClient());

        await using (var ctx = db.CreateContext())
        {
            var c = await Classes(ctx, db).CreateAsync(new ClassInput("SS1", "2026/2027"));
            c.Name.Should().Be("SS1");
        }
        await using (var ctx = db.CreateContext())
        {
            var dup = () => Classes(ctx, db).CreateAsync(new ClassInput("SS1", "2026/2027"));
            await dup.Should().ThrowAsync<ConflictException>();
        }
    }

    [Fact]
    public async Task Create_student_provisions_a_dedicated_virtual_account()
    {
        using var db = new TestDb();
        var xental = new FakeXentalClient();
        await SeedSchoolAsync(db, xental);
        Guid classId;
        await using (var ctx = db.CreateContext())
            classId = (await Classes(ctx, db).CreateAsync(new ClassInput("SS1", "2026/2027"))).Id;

        await using (var ctx = db.CreateContext())
        {
            var s = await Students(ctx, db, xental, new FakeNotificationSender())
                .CreateAsync(new StudentInput("ADM-001", "Ada Lovelace", classId, null, "Mrs Lovelace", "08099998888", "mum@x.com"));
            s.HasVirtualAccount.Should().BeTrue();
            s.Nuban.Should().NotBeNullOrEmpty();
            s.AccountName.Should().Be("PayLibre - Ada Lovelace");
        }
        xental.VirtualAccountsCreated.Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_admission_number_conflicts()
    {
        using var db = new TestDb();
        var xental = new FakeXentalClient();
        await SeedSchoolAsync(db, xental);
        Guid classId;
        await using (var ctx = db.CreateContext())
            classId = (await Classes(ctx, db).CreateAsync(new ClassInput("SS1", "2026/2027"))).Id;

        await using (var ctx = db.CreateContext())
            await Students(ctx, db, xental, new FakeNotificationSender())
                .CreateAsync(new StudentInput("ADM-001", "Ada", classId, null, "Guardian", null, null));
        await using (var ctx = db.CreateContext())
        {
            var dup = () => Students(ctx, db, xental, new FakeNotificationSender())
                .CreateAsync(new StudentInput("ADM-001", "Bob", classId, null, "Guardian", null, null));
            await dup.Should().ThrowAsync<ConflictException>();
        }
    }

    [Fact]
    public async Task Import_csv_creates_valid_rows_and_reports_the_bad_ones()
    {
        using var db = new TestDb();
        var xental = new FakeXentalClient();
        await SeedSchoolAsync(db, xental);
        await using (var ctx = db.CreateContext())
            await Classes(ctx, db).CreateAsync(new ClassInput("SS1", "2026/2027"));

        var csv = string.Join("\n",
            "AdmissionNo,FullName,Class,Session,GuardianName,GuardianPhone,GuardianEmail",
            "ADM-100,Ada Lovelace,SS1,2026/2027,Mrs Lovelace,08011112222,mum@x.com",
            "ADM-101,Grace Hopper,SS1,2026/2027,Mr Hopper,,dad@x.com",
            "ADM-100,Duplicate Ada,SS1,2026/2027,Someone,,",          // duplicate admission
            "ADM-102,No Class,JSS9,2026/2027,Guardian,,");            // unknown class

        ImportResult result;
        await using (var ctx = db.CreateContext())
            result = await Students(ctx, db, xental, new FakeNotificationSender()).ImportCsvAsync(csv);

        result.Created.Should().Be(2);
        result.Failed.Should().Be(2);
        result.Errors.Should().HaveCount(2);
        xental.VirtualAccountsCreated.Should().Be(2);

        await using var check = db.CreateContext();
        (await check.Students.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Send_account_details_invokes_the_notifier()
    {
        using var db = new TestDb();
        var xental = new FakeXentalClient();
        var notifier = new FakeNotificationSender();
        await SeedSchoolAsync(db, xental);
        Guid classId, studentId;
        await using (var ctx = db.CreateContext())
            classId = (await Classes(ctx, db).CreateAsync(new ClassInput("SS1", "2026/2027"))).Id;
        await using (var ctx = db.CreateContext())
            studentId = (await Students(ctx, db, xental, notifier)
                .CreateAsync(new StudentInput("ADM-1", "Ada", classId, null, "Guardian", "0800", "g@x.com"))).Id;

        await using (var ctx = db.CreateContext())
            await Students(ctx, db, xental, notifier).SendAccountDetailsAsync(studentId);
        notifier.Sent.Should().Be(1);
    }
}
