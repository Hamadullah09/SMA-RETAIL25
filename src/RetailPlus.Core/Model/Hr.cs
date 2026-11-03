namespace RetailPlus.Core.Model;

// Human resources and payroll (phase E3). Every table here is kept on the server only: employee records, salaries and
// payslips never travel to the tills. The time clock that attendance comes from stays where it was (TimeClockEntry), and
// people sign in at the tills with their user account, which an employee record may point at.

// Country rules -----------------------------------------------------------------------------------------------------

/// <summary>What the provincial social security contribution is worked out on.</summary>
public enum SocialSecurityBasis
{
    /// <summary>The wage earned in the month.</summary>
    Wage,
    /// <summary>The minimum wage of the rule set, whatever the employee earns.</summary>
    MinimumWage,
}

/// <summary>How lateness is taken off pay.</summary>
public enum LatenessRule
{
    None,
    /// <summary>A day's pay for every <see cref="PayrollRuleSet.LatesPerDay"/> late arrivals in the month.</summary>
    DayPerLates,
    /// <summary>Every minute late, at the hourly rate.</summary>
    PerMinute,
}

/// <summary>What a month's pay is divided by to give a day's pay.</summary>
public enum DailyRateBasis
{
    /// <summary>The days of the month (28 to 31).</summary>
    CalendarDays,
    /// <summary>Always 30.</summary>
    ThirtyDays,
    /// <summary>The working days of the month by the employee's shifts.</summary>
    WorkingDays,
}

/// <summary>
/// A country's payroll rules for a time: income tax slabs on annual taxable income, the minimum wage, pension (EOBI) and
/// provincial social security contributions, the working week, overtime, deductions for absence and lateness, gratuity,
/// the tax treatment of allowances, and the formats of identity numbers and bank accounts. Each country has a row per
/// period of effect (a budget year); the row in effect on a payroll month's last day is used. Every figure is data,
/// changed on the web; Pakistan is seeded.
/// </summary>
public class PayrollRuleSet : Entity, ILocalOnly, IVersioned
{
    /// <summary>ISO country code (PK).</summary>
    public string Country { get; set; } = "PK";
    public string Name { get; set; } = "";
    public DateOnly EffectiveFrom { get; set; }
    /// <summary>Last day in effect; null until a later rule set takes over.</summary>
    public DateOnly? EffectiveTo { get; set; }
    /// <summary>The month the tax year starts in (Pakistan: 7, July).</summary>
    public int TaxYearStartMonth { get; set; } = 7;
    /// <summary>The monthly minimum wage.</summary>
    public decimal MinimumWage { get; set; }
    /// <summary>Employer's pension contribution (EOBI), percent of the minimum wage.</summary>
    public decimal EobiEmployerRate { get; set; }
    /// <summary>Employee's pension contribution (EOBI), percent of the minimum wage.</summary>
    public decimal EobiEmployeeRate { get; set; }
    /// <summary>Which province's social security institution the rates are for (Punjab, PESSI).</summary>
    public string? SocialSecurityProvince { get; set; }
    /// <summary>Employer's social security contribution, percent.</summary>
    public decimal SocialSecurityRate { get; set; }
    /// <summary>Employees whose monthly wage is above this are not covered; 0 covers everyone.</summary>
    public decimal SocialSecurityWageCeiling { get; set; }
    public SocialSecurityBasis SocialSecurityBasis { get; set; }
    public int WorkingDaysPerWeek { get; set; } = 6;
    public decimal HoursPerWeek { get; set; } = 48;
    /// <summary>Overtime is paid at the hourly rate times this (2 for double).</summary>
    public decimal OvertimeMultiplier { get; set; } = 2;
    /// <summary>Overtime of a day counts only from this many minutes.</summary>
    public int OvertimeMinimumMinutes { get; set; } = 30;
    public DailyRateBasis DailyRateBasis { get; set; }
    public LatenessRule LatenessRule { get; set; }
    /// <summary>For <see cref="LatenessRule.DayPerLates"/>: late arrivals that cost a day's pay.</summary>
    public int LatesPerDay { get; set; } = 3;
    /// <summary>Days absent without leave are not paid.</summary>
    public bool DeductAbsences { get; set; } = true;
    /// <summary>Annual taxable income above which the tax carries a surcharge; 0 for none.</summary>
    public decimal SurchargeThreshold { get; set; }
    /// <summary>The surcharge, percent of the tax.</summary>
    public decimal SurchargeRate { get; set; }
    public bool GratuityEnabled { get; set; }
    /// <summary>Days' wages of gratuity for every year of service.</summary>
    public decimal GratuityDaysPerYear { get; set; } = 30;
    /// <summary>Service needed before any gratuity is due, in months.</summary>
    public int GratuityMinimumMonths { get; set; } = 12;
    /// <summary>A part year longer than this many months counts as a whole year.</summary>
    public int GratuityPartYearMonths { get; set; } = 6;
    /// <summary>Gratuity on the last basic pay (true) or on basic and allowances (false).</summary>
    public bool GratuityOnBasic { get; set; }
    /// <summary>A regular expression the national identity number must match (Pakistan's CNIC: 00000-0000000-0).</summary>
    public string? CnicPattern { get; set; }
    public string? CnicExample { get; set; }
    /// <summary>A regular expression a bank account (IBAN) of the country must match; the IBAN check digits are checked too.</summary>
    public string? IbanPattern { get; set; }
    public string? IbanExample { get; set; }
    /// <summary>Where the figures come from and when to check them again.</summary>
    public string? Notes { get; set; }
    public Guid Version { get; set; }
    public List<PayrollTaxSlab> TaxSlabs { get; set; } = [];
    public List<PayrollAllowanceRule> Allowances { get; set; } = [];
}

/// <summary>
/// One income tax slab: on annual taxable income above <see cref="IncomeFrom"/> (and up to <see cref="IncomeTo"/>), the
/// tax is <see cref="FixedTax"/> plus <see cref="Rate"/> percent of the amount above <see cref="IncomeFrom"/>.
/// </summary>
public class PayrollTaxSlab : Entity, ILocalOnly
{
    public Guid RuleSetId { get; set; }
    public decimal IncomeFrom { get; set; }
    /// <summary>Null for the top slab.</summary>
    public decimal? IncomeTo { get; set; }
    public decimal FixedTax { get; set; }
    public decimal Rate { get; set; }
}

/// <summary>
/// How an allowance is taxed under a rule set, by its code (HRA, MEDICAL…): taxable, or exempt up to a share of the
/// basic pay (the rest is taxable), or wholly exempt. Salary lines with a code the rule set does not list are taxable.
/// </summary>
public class PayrollAllowanceRule : Entity, ILocalOnly
{
    public Guid RuleSetId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsTaxable { get; set; } = true;
    /// <summary>For an exempt allowance: exempt up to this percent of basic pay; null exempts all of it.</summary>
    public decimal? ExemptPercentOfBasic { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>What a leave type is, for the rules that depend on it.</summary>
public enum LeaveKind
{
    Annual,
    Casual,
    Sick,
    Maternity,
    Paternity,
    Unpaid,
    Other,
}

public enum Gender
{
    Male,
    Female,
    Other,
}

/// <summary>
/// A kind of leave of a country (annual, casual, sick, maternity, paternity, unpaid): the days a year, whether it is paid,
/// whether it is earned month by month, how much may be carried into the next year, and whether what is left is paid out
/// when someone leaves. Kept per country so balances run on across rule sets.
/// </summary>
public class LeaveType : Entity, ILocalOnly
{
    public string Country { get; set; } = "PK";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public LeaveKind Kind { get; set; }
    /// <summary>The year's entitlement; 0 for leave without a limit (unpaid).</summary>
    public decimal DaysPerYear { get; set; }
    public bool IsPaid { get; set; } = true;
    /// <summary>The entitlement is earned a twelfth each month worked, rather than all at the start of the year.</summary>
    public bool AccruesMonthly { get; set; }
    /// <summary>Days left at the end of a year that are carried into the next; 0 for none.</summary>
    public decimal CarryOverMaxDays { get; set; }
    /// <summary>Days left are paid out in the final settlement.</summary>
    public bool Encashable { get; set; }
    /// <summary>Counts every day of the request (maternity), not only working days.</summary>
    public bool CountsCalendarDays { get; set; }
    /// <summary>Only for employees of this gender (maternity, paternity).</summary>
    public Gender? OnlyFor { get; set; }
    /// <summary>Months of service before the leave may be taken.</summary>
    public int MinimumServiceMonths { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>A public holiday of a country: nobody is absent on it, and work on it is overtime.</summary>
public class Holiday : Entity, ILocalOnly
{
    public string Country { get; set; } = "PK";
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
}

// Lists ---------------------------------------------------------------------------------------------------------------

/// <summary>A department of the organisation (Sales floor, Warehouse, Accounts), apart from the item departments.</summary>
public class HrDepartment : Entity, ILocalOnly
{
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

/// <summary>A job title (Cashier, Store manager).</summary>
public class Designation : Entity, ILocalOnly
{
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

// Employees -----------------------------------------------------------------------------------------------------------

public enum MaritalStatus
{
    Unmarried,
    Married,
    Divorced,
    Widowed,
}

public enum EmploymentType
{
    Permanent,
    Contract,
    Probation,
    /// <summary>Paid by the day worked: the salary's basic is the day's wage.</summary>
    DailyWage,
}

/// <summary>How the net pay reaches the employee: on the bank transfer list or on the cash sheet.</summary>
public enum SalaryPaymentMethod
{
    Cash,
    Bank,
}

/// <summary>
/// A person who works for the company: who they are, their job, how they are paid and their statutory numbers. An
/// employee who also uses the tills points at their <see cref="UserAccount"/>, whose time clock entries make their
/// attendance and whose sales make their commission.
/// </summary>
public class Employee : Entity, ILocalOnly, IVersioned
{
    /// <summary>The employee number people know (E0001); unique.</summary>
    public string Code { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? FatherOrHusbandName { get; set; }
    /// <summary>National identity number, in the format of the country's rule set.</summary>
    public string? Cnic { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public MaritalStatus? MaritalStatus { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelation { get; set; }

    public DateOnly JoiningDate { get; set; }
    public DateOnly? ConfirmationDate { get; set; }
    /// <summary>The last day worked; paid up to it.</summary>
    public DateOnly? LeavingDate { get; set; }
    public string? LeavingReason { get; set; }
    public EmploymentType EmploymentType { get; set; }
    public Guid? HrDepartmentId { get; set; }
    public Guid? DesignationId { get; set; }
    /// <summary>The store the employee works at; null for head office.</summary>
    public Guid? StoreId { get; set; }
    /// <summary>The employee they report to, who may approve their leave.</summary>
    public Guid? ManagerId { get; set; }
    /// <summary>Their user at the tills, whose punches and sales count; unique.</summary>
    public Guid? UserAccountId { get; set; }
    /// <summary>The usual shift, when the roster names none for the week.</summary>
    public Guid? ShiftId { get; set; }
    /// <summary>Attendance comes from the time clock; false presumes them present on working days.</summary>
    public bool TracksAttendance { get; set; } = true;
    public bool OvertimeEligible { get; set; } = true;
    public bool EobiApplies { get; set; } = true;
    public bool SocialSecurityApplies { get; set; } = true;

    public SalaryPaymentMethod PaymentMethod { get; set; }
    public string? BankName { get; set; }
    public string? Iban { get; set; }
    /// <summary>National tax number.</summary>
    public string? Ntn { get; set; }
    public string? EobiNumber { get; set; }
    public string? SocialSecurityNumber { get; set; }
    /// <summary>The tax year (by the calendar year it starts in) of the figures from a previous employer below.</summary>
    public int? PriorTaxYear { get; set; }
    /// <summary>Taxable income earned this tax year before joining.</summary>
    public decimal PriorTaxableIncome { get; set; }
    /// <summary>Income tax deducted this tax year before joining.</summary>
    public decimal PriorTaxDeducted { get; set; }
    public string? Notes { get; set; }
    public Guid Version { get; set; }

    /// <summary>Works for the company on <paramref name="day"/>: joined by then and not left before it.</summary>
    public bool IsEmployedOn(DateOnly day) => JoiningDate <= day && (LeavingDate is null || LeavingDate >= day);
}

/// <summary>A file kept with an employee's record (CNIC copy, contract, certificates).</summary>
public class EmployeeDocument : Entity, ILocalOnly
{
    public Guid EmployeeId { get; set; }
    public string Title { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public byte[] Data { get; set; } = [];
    public string? UploadedBy { get; set; }
}

// Shifts and attendance -----------------------------------------------------------------------------------------------

/// <summary>The days of the week a shift is worked.</summary>
[Flags]
public enum WorkDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    MondayToFriday = Monday | Tuesday | Wednesday | Thursday | Friday,
    MondayToSaturday = MondayToFriday | Saturday,
    Everyday = MondayToSaturday | Sunday,
}

/// <summary>
/// A shift: when it starts and ends (an end before the start is the next day), its unpaid break, the minutes of grace
/// before someone counts as late or as leaving early, and the days of the week it is worked.
/// </summary>
public class Shift : Entity, ILocalOnly
{
    public string Name { get; set; } = "";
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int BreakMinutes { get; set; }
    public int GraceMinutes { get; set; }
    public WorkDays Days { get; set; } = WorkDays.MondayToSaturday;
    /// <summary>The store it is for; null for every store.</summary>
    public Guid? StoreId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>The roster: the shift an employee works in a week (starting on its Monday).</summary>
public class RosterWeek : Entity, ILocalOnly
{
    public Guid EmployeeId { get; set; }
    public DateOnly WeekStart { get; set; }
    public Guid ShiftId { get; set; }
}

/// <summary>An exception to the roster on one day: another shift, or a day off (no shift).</summary>
public class RosterDay : Entity, ILocalOnly
{
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    /// <summary>Null: a day off.</summary>
    public Guid? ShiftId { get; set; }
    public string? Note { get; set; }
}

/// <summary>What a day was for an employee.</summary>
public enum AttendanceStatus
{
    Present,
    Absent,
    /// <summary>On approved leave.</summary>
    Leave,
    /// <summary>A public holiday.</summary>
    Holiday,
    /// <summary>Not a working day (no shift).</summary>
    Off,
    /// <summary>A working day still to come.</summary>
    Scheduled,
}

/// <summary>Where a request waiting for someone's decision stands.</summary>
public enum HrApproval
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>
/// A correction of a day's attendance (a forgotten punch, work away from the store): what the day should say, with a
/// reason. It counts once someone else with <c>hr.attendance</c> approves it.
/// </summary>
public class AttendanceCorrection : Entity, ILocalOnly
{
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public AttendanceStatus Status { get; set; }
    public int WorkedMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public string Reason { get; set; } = "";
    public HrApproval Approval { get; set; }
    public string? RequestedBy { get; set; }
    public Guid? RequestedById { get; set; }
    public string? DecidedBy { get; set; }
    public Guid? DecidedById { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionNote { get; set; }
}

// Leave ---------------------------------------------------------------------------------------------------------------

public enum LeaveRequestStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled,
}

/// <summary>
/// A request for leave from one day to another, with half days at either end. Approved by the employee's manager or by
/// someone with <c>hr.leave</c>; approved leave shows in attendance, and unpaid leave is taken off pay.
/// </summary>
public class LeaveRequest : Entity, ILocalOnly
{
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    /// <summary>The first day is a half day (the afternoon).</summary>
    public bool FirstDayHalf { get; set; }
    /// <summary>The last day is a half day (the morning).</summary>
    public bool LastDayHalf { get; set; }
    /// <summary>Days of leave the request takes (working days, or every day for leave that counts calendar days).</summary>
    public decimal Days { get; set; }
    public string? Reason { get; set; }
    public LeaveRequestStatus Status { get; set; }
    public string? RequestedBy { get; set; }
    public Guid? RequestedById { get; set; }
    public string? DecidedBy { get; set; }
    public Guid? DecidedById { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionNote { get; set; }
}

/// <summary>Days added to (or taken from) a leave balance of one year by hand: an opening balance, a correction.</summary>
public class LeaveAdjustment : Entity, ILocalOnly
{
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public int Year { get; set; }
    public decimal Days { get; set; }
    public string Reason { get; set; } = "";
    public string? MadeBy { get; set; }
}

// Salary, loans and payroll ---------------------------------------------------------------------------------------------

public enum SalaryComponentKind
{
    Allowance,
    /// <summary>A fixed deduction every month (a fund, a fee).</summary>
    Deduction,
}

/// <summary>
/// An employee's pay from a date: the monthly basic (for daily wage, the day's wage), allowances and fixed deductions.
/// A change is a new row, so the history stays; the row in effect on the last day paid is used.
/// </summary>
public class SalaryStructure : Entity, ILocalOnly
{
    public Guid EmployeeId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public decimal Basic { get; set; }
    public string? Note { get; set; }
    public string? ChangedBy { get; set; }
    public List<SalaryComponent> Components { get; set; } = [];
}

/// <summary>An allowance or fixed deduction of a salary, a month's amount, by code (HRA, MEDICAL, CONVEYANCE, UTILITIES…).</summary>
public class SalaryComponent : Entity, ILocalOnly
{
    public Guid SalaryStructureId { get; set; }
    public int LineNumber { get; set; }
    public SalaryComponentKind Kind { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    /// <summary>For a deduction: the ledger account it is owed to; null keeps it in salaries payable.</summary>
    public string? AccountCode { get; set; }
}

public enum LoanKind
{
    Loan,
    /// <summary>An advance against salary.</summary>
    Advance,
}

public enum LoanStatus
{
    Active,
    /// <summary>Recovered in full.</summary>
    Settled,
    Cancelled,
}

/// <summary>A loan or salary advance paid to an employee and recovered from their pay in monthly instalments.</summary>
public class EmployeeLoan : Entity, ILocalOnly
{
    public long Number { get; set; }
    public Guid EmployeeId { get; set; }
    public LoanKind Kind { get; set; }
    public DateOnly IssuedOn { get; set; }
    public decimal Amount { get; set; }
    public decimal Instalment { get; set; }
    /// <summary>The first month an instalment is taken (its first day).</summary>
    public DateOnly FirstMonth { get; set; }
    public LoanStatus Status { get; set; }
    public string? Reason { get; set; }
    /// <summary>The account it was paid from.</summary>
    public string? PaidFromAccount { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? CreatedBy { get; set; }
    public List<LoanInstalment> Instalments { get; set; } = [];
}

/// <summary>One month's instalment of a loan, and what payroll recovered of it.</summary>
public class LoanInstalment : Entity, ILocalOnly
{
    public Guid LoanId { get; set; }
    public int Number { get; set; }
    /// <summary>The month it is due (its first day).</summary>
    public DateOnly Month { get; set; }
    public decimal Amount { get; set; }
    public decimal Recovered { get; set; }
    /// <summary>The payroll run that recovered it, once posted.</summary>
    public Guid? PayrollRunId { get; set; }
}

public enum PayrollRunKind
{
    Monthly,
    /// <summary>One employee's last pay when they leave: days worked, leave paid out, gratuity, loans recovered.</summary>
    FinalSettlement,
}

/// <summary>Draft → Calculated → Approved → Posted (to the ledger) → Paid.</summary>
public enum PayrollRunStatus
{
    Draft,
    Calculated,
    Approved,
    Posted,
    Paid,
}

/// <summary>
/// A month's payroll for one store or (no store) the company, or a final settlement for one employee. Calculating makes a
/// payslip for every employee in it; approving freezes it; posting puts it in the general ledger (source
/// <c>payroll:{id}</c>) and recovers loan instalments; paying records the transfer and cash payments.
/// </summary>
public class PayrollRun : Entity, ILocalOnly, IVersioned
{
    public long Number { get; set; }
    public PayrollRunKind Kind { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    /// <summary>The store whose employees it pays; null for the whole company.</summary>
    public Guid? StoreId { get; set; }
    /// <summary>For a final settlement: the employee who leaves.</summary>
    public Guid? EmployeeId { get; set; }
    public PayrollRunStatus Status { get; set; }
    public string? Notes { get; set; }
    /// <summary>What the calculation noted: employees left out and why.</summary>
    public string? Warnings { get; set; }
    public int Employees { get; set; }
    public decimal Gross { get; set; }
    public decimal Deductions { get; set; }
    public decimal Net { get; set; }
    public decimal Tax { get; set; }
    public decimal EmployerContributions { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CalculatedAtUtc { get; set; }
    public string? CalculatedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public string? PostedBy { get; set; }
    public Guid? JournalEntryId { get; set; }
    public DateOnly? PaidOn { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public string? PaidBy { get; set; }
    public Guid? PaymentEntryId { get; set; }
    public Guid Version { get; set; }
}

/// <summary>
/// An employee's pay for a run, with what it was worked out from (days, leave, lateness, overtime) and the tax
/// projection, kept as it was calculated. The employee's name, job and bank are copied so the payslip stays as issued.
/// </summary>
public class Payslip : Entity, ILocalOnly
{
    public Guid PayrollRunId { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string? Designation { get; set; }
    public string? Department { get; set; }
    public Guid? StoreId { get; set; }
    public string? Cnic { get; set; }
    public SalaryPaymentMethod PaymentMethod { get; set; }
    public string? BankName { get; set; }
    public string? Iban { get; set; }
    public int DaysInPeriod { get; set; }
    public int DaysEmployed { get; set; }
    public decimal DaysPresent { get; set; }
    public decimal PaidLeaveDays { get; set; }
    public decimal UnpaidLeaveDays { get; set; }
    public int AbsentDays { get; set; }
    public int LateDays { get; set; }
    public int LateMinutes { get; set; }
    public decimal OvertimeHours { get; set; }
    /// <summary>The month's basic of the salary used (for daily wage, the day's wage).</summary>
    public decimal Basic { get; set; }
    public decimal Gross { get; set; }
    public decimal Deductions { get; set; }
    public decimal Net { get; set; }
    /// <summary>Taxable income of this payslip.</summary>
    public decimal Taxable { get; set; }
    public decimal Tax { get; set; }
    public decimal EmployerContributions { get; set; }
    /// <summary>Taxable income and tax of the tax year before this payslip (earlier payslips and a previous employer).</summary>
    public decimal TaxableBefore { get; set; }
    public decimal TaxBefore { get; set; }
    /// <summary>The year's taxable income as projected, and the tax on it.</summary>
    public decimal ProjectedAnnualTaxable { get; set; }
    public decimal ProjectedAnnualTax { get; set; }
    public List<PayslipLine> Lines { get; set; } = [];
}

public enum PayslipLineKind
{
    Earning,
    Deduction,
    /// <summary>Paid by the employer on top of the pay (pension, social security); not taken from the employee.</summary>
    EmployerContribution,
}

/// <summary>One line of a payslip: an earning, a deduction or an employer contribution.</summary>
public class PayslipLine : Entity, ILocalOnly
{
    public Guid PayslipId { get; set; }
    public int LineNumber { get; set; }
    public PayslipLineKind Kind { get; set; }
    /// <summary>What it is: BASIC, an allowance code, OVERTIME, COMMISSION, UNPAID, ABSENT, LATE, TAX, EOBI, LOAN, EOBI-ER, SS-ER…</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    /// <summary>Days or hours the amount is for, when it is worked out from them.</summary>
    public decimal? Quantity { get; set; }
    public decimal? Rate { get; set; }
    /// <summary>Part of the taxable income (earnings), or taken off it (attendance deductions).</summary>
    public decimal TaxableAmount { get; set; }
    public Guid? LoanInstalmentId { get; set; }
    /// <summary>For a fixed deduction: the ledger account it is owed to.</summary>
    public string? AccountCode { get; set; }
}

/// <summary>The HR tables, for the rules that treat them alike (their audit lines stay on the server).</summary>
public static class HrTables
{
    public static IReadOnlyList<string> Names { get; } =
    [
        nameof(PayrollRuleSet), nameof(PayrollTaxSlab), nameof(PayrollAllowanceRule), nameof(LeaveType), nameof(Holiday), nameof(HrDepartment),
        nameof(Designation), nameof(Employee), nameof(EmployeeDocument), nameof(Shift), nameof(RosterWeek), nameof(RosterDay),
        nameof(AttendanceCorrection), nameof(LeaveRequest), nameof(LeaveAdjustment), nameof(SalaryStructure), nameof(SalaryComponent),
        nameof(EmployeeLoan), nameof(LoanInstalment), nameof(PayrollRun), nameof(Payslip), nameof(PayslipLine),
    ];
}
