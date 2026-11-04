using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Configuration;

internal sealed class PayrollRuleSetConfiguration : IEntityTypeConfiguration<PayrollRuleSet>
{
    public void Configure(EntityTypeBuilder<PayrollRuleSet> e)
    {
        e.Property(x => x.Country).HasMaxLength(2);
        e.Property(x => x.Name).HasMaxLength(80);
        e.Property(x => x.EobiEmployerRate).HasPrecision(9, 4);
        e.Property(x => x.EobiEmployeeRate).HasPrecision(9, 4);
        e.Property(x => x.SocialSecurityProvince).HasMaxLength(60);
        e.Property(x => x.SocialSecurityRate).HasPrecision(9, 4);
        e.Property(x => x.SocialSecurityBasis).HasConversion<string>().HasMaxLength(12);
        e.Property(x => x.HoursPerWeek).HasPrecision(7, 2);
        e.Property(x => x.OvertimeMultiplier).HasPrecision(7, 3);
        e.Property(x => x.DailyRateBasis).HasConversion<string>().HasMaxLength(14);
        e.Property(x => x.LatenessRule).HasConversion<string>().HasMaxLength(12);
        e.Property(x => x.SurchargeThreshold).HasPrecision(14, 2);
        e.Property(x => x.SurchargeRate).HasPrecision(9, 4);
        e.Property(x => x.GratuityDaysPerYear).HasPrecision(7, 2);
        e.Property(x => x.CnicPattern).HasMaxLength(200);
        e.Property(x => x.CnicExample).HasMaxLength(40);
        e.Property(x => x.IbanPattern).HasMaxLength(200);
        e.Property(x => x.IbanExample).HasMaxLength(40);
        e.Property(x => x.Notes).HasMaxLength(2000);
        e.HasIndex(x => new { x.Country, x.EffectiveFrom }).IsUnique();
        e.HasMany(x => x.TaxSlabs).WithOne().HasForeignKey(x => x.RuleSetId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.Allowances).WithOne().HasForeignKey(x => x.RuleSetId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PayrollTaxSlabConfiguration : IEntityTypeConfiguration<PayrollTaxSlab>
{
    public void Configure(EntityTypeBuilder<PayrollTaxSlab> e)
    {
        e.Property(x => x.IncomeFrom).HasPrecision(14, 2);
        e.Property(x => x.IncomeTo).HasPrecision(14, 2);
        e.Property(x => x.FixedTax).HasPrecision(14, 2);
        e.Property(x => x.Rate).HasPrecision(9, 4);
        e.HasIndex(x => new { x.RuleSetId, x.IncomeFrom }).IsUnique();
    }
}

internal sealed class PayrollAllowanceRuleConfiguration : IEntityTypeConfiguration<PayrollAllowanceRule>
{
    public void Configure(EntityTypeBuilder<PayrollAllowanceRule> e)
    {
        e.Property(x => x.Code).HasMaxLength(20);
        e.Property(x => x.Name).HasMaxLength(60);
        e.Property(x => x.ExemptPercentOfBasic).HasPrecision(9, 4);
        e.HasIndex(x => new { x.RuleSetId, x.Code }).IsUnique();
    }
}

internal sealed class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> e)
    {
        e.Property(x => x.Country).HasMaxLength(2);
        e.Property(x => x.Code).HasMaxLength(10);
        e.Property(x => x.Name).HasMaxLength(60);
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12);
        e.Property(x => x.DaysPerYear).HasPrecision(7, 2);
        e.Property(x => x.CarryOverMaxDays).HasPrecision(7, 2);
        e.Property(x => x.OnlyFor).HasConversion<string>().HasMaxLength(8);
        e.HasIndex(x => new { x.Country, x.Code }).IsUnique();
    }
}

internal sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> e)
    {
        e.Property(x => x.Country).HasMaxLength(2);
        e.Property(x => x.Name).HasMaxLength(80);
        e.HasIndex(x => new { x.Country, x.Date }).IsUnique();
    }
}

internal sealed class HrDepartmentConfiguration : IEntityTypeConfiguration<HrDepartment>
{
    public void Configure(EntityTypeBuilder<HrDepartment> e)
    {
        e.Property(x => x.Name).HasMaxLength(60);
        e.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class DesignationConfiguration : IEntityTypeConfiguration<Designation>
{
    public void Configure(EntityTypeBuilder<Designation> e)
    {
        e.Property(x => x.Name).HasMaxLength(60);
        e.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> e)
    {
        e.Property(x => x.Code).HasMaxLength(20);
        e.Property(x => x.FullName).HasMaxLength(100);
        e.Property(x => x.FatherOrHusbandName).HasMaxLength(100);
        e.Property(x => x.Cnic).HasMaxLength(30);
        e.Property(x => x.Gender).HasConversion<string>().HasMaxLength(8);
        e.Property(x => x.MaritalStatus).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Address).HasMaxLength(200);
        e.Property(x => x.City).HasMaxLength(60);
        e.Property(x => x.Phone).HasMaxLength(30);
        e.Property(x => x.Email).HasMaxLength(100);
        e.Property(x => x.EmergencyContactName).HasMaxLength(100);
        e.Property(x => x.EmergencyContactPhone).HasMaxLength(30);
        e.Property(x => x.EmergencyContactRelation).HasMaxLength(40);
        e.Property(x => x.LeavingReason).HasMaxLength(200);
        e.Property(x => x.EmploymentType).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(6);
        e.Property(x => x.BankName).HasMaxLength(60);
        e.Property(x => x.Iban).HasMaxLength(34);
        e.Property(x => x.Ntn).HasMaxLength(20);
        e.Property(x => x.EobiNumber).HasMaxLength(30);
        e.Property(x => x.SocialSecurityNumber).HasMaxLength(30);
        e.Property(x => x.PriorTaxableIncome).HasPrecision(14, 2);
        e.Property(x => x.PriorTaxDeducted).HasPrecision(14, 2);
        e.Property(x => x.Notes).HasMaxLength(1000);
        e.HasIndex(x => x.Code).IsUnique();
        e.HasIndex(x => x.UserAccountId).IsUnique().HasFilter("[UserAccountId] IS NOT NULL");
        e.HasIndex(x => x.FullName);
        e.HasOne<HrDepartment>().WithMany().HasForeignKey(x => x.HrDepartmentId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Designation>().WithMany().HasForeignKey(x => x.DesignationId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserAccountId).OnDelete(DeleteBehavior.SetNull);
        e.HasOne<Shift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EmployeeDocumentConfiguration : IEntityTypeConfiguration<EmployeeDocument>
{
    public void Configure(EntityTypeBuilder<EmployeeDocument> e)
    {
        e.Property(x => x.Title).HasMaxLength(100);
        e.Property(x => x.FileName).HasMaxLength(200);
        e.Property(x => x.ContentType).HasMaxLength(100);
        e.Property(x => x.UploadedBy).HasMaxLength(50);
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> e)
    {
        e.Property(x => x.Name).HasMaxLength(60);
        e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RosterWeekConfiguration : IEntityTypeConfiguration<RosterWeek>
{
    public void Configure(EntityTypeBuilder<RosterWeek> e)
    {
        e.HasIndex(x => new { x.EmployeeId, x.WeekStart }).IsUnique();
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<Shift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RosterDayConfiguration : IEntityTypeConfiguration<RosterDay>
{
    public void Configure(EntityTypeBuilder<RosterDay> e)
    {
        e.Property(x => x.Note).HasMaxLength(200);
        e.HasIndex(x => new { x.EmployeeId, x.Date }).IsUnique();
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<Shift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AttendanceCorrectionConfiguration : IEntityTypeConfiguration<AttendanceCorrection>
{
    public void Configure(EntityTypeBuilder<AttendanceCorrection> e)
    {
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Approval).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Reason).HasMaxLength(200);
        e.Property(x => x.RequestedBy).HasMaxLength(50);
        e.Property(x => x.DecidedBy).HasMaxLength(50);
        e.Property(x => x.DecisionNote).HasMaxLength(200);
        e.HasIndex(x => new { x.EmployeeId, x.Date });
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> e)
    {
        e.Property(x => x.Days).HasPrecision(7, 2);
        e.Property(x => x.Reason).HasMaxLength(200);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.RequestedBy).HasMaxLength(50);
        e.Property(x => x.DecidedBy).HasMaxLength(50);
        e.Property(x => x.DecisionNote).HasMaxLength(200);
        e.HasIndex(x => new { x.EmployeeId, x.FromDate });
        e.HasIndex(x => new { x.Status, x.FromDate });
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<LeaveType>().WithMany().HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LeaveAdjustmentConfiguration : IEntityTypeConfiguration<LeaveAdjustment>
{
    public void Configure(EntityTypeBuilder<LeaveAdjustment> e)
    {
        e.Property(x => x.Days).HasPrecision(7, 2);
        e.Property(x => x.Reason).HasMaxLength(200);
        e.Property(x => x.MadeBy).HasMaxLength(50);
        e.HasIndex(x => new { x.EmployeeId, x.Year });
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<LeaveType>().WithMany().HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalaryStructureConfiguration : IEntityTypeConfiguration<SalaryStructure>
{
    public void Configure(EntityTypeBuilder<SalaryStructure> e)
    {
        e.Property(x => x.Note).HasMaxLength(200);
        e.Property(x => x.ChangedBy).HasMaxLength(50);
        e.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom }).IsUnique();
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.Components).WithOne().HasForeignKey(x => x.SalaryStructureId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SalaryComponentConfiguration : IEntityTypeConfiguration<SalaryComponent>
{
    public void Configure(EntityTypeBuilder<SalaryComponent> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Code).HasMaxLength(20);
        e.Property(x => x.Name).HasMaxLength(60);
        e.Property(x => x.AccountCode).HasMaxLength(20);
    }
}

internal sealed class EmployeeLoanConfiguration : IEntityTypeConfiguration<EmployeeLoan>
{
    public void Configure(EntityTypeBuilder<EmployeeLoan> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(8);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Reason).HasMaxLength(200);
        e.Property(x => x.PaidFromAccount).HasMaxLength(20);
        e.Property(x => x.CreatedBy).HasMaxLength(50);
        e.HasIndex(x => x.Number);
        e.HasIndex(x => new { x.EmployeeId, x.Status });
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Instalments).WithOne().HasForeignKey(x => x.LoanId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LoanInstalmentConfiguration : IEntityTypeConfiguration<LoanInstalment>
{
    public void Configure(EntityTypeBuilder<LoanInstalment> e)
    {
        e.HasIndex(x => new { x.LoanId, x.Number }).IsUnique();
        e.HasIndex(x => x.Month);
    }
}

internal sealed class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        e.Property(x => x.Notes).HasMaxLength(400);
        e.Property(x => x.Warnings).HasMaxLength(4000);
        e.Property(x => x.Gross).HasPrecision(16, 2);
        e.Property(x => x.Deductions).HasPrecision(16, 2);
        e.Property(x => x.Net).HasPrecision(16, 2);
        e.Property(x => x.Tax).HasPrecision(16, 2);
        e.Property(x => x.EmployerContributions).HasPrecision(16, 2);
        e.Property(x => x.CreatedBy).HasMaxLength(50);
        e.Property(x => x.CalculatedBy).HasMaxLength(50);
        e.Property(x => x.ApprovedBy).HasMaxLength(50);
        e.Property(x => x.PostedBy).HasMaxLength(50);
        e.Property(x => x.PaidBy).HasMaxLength(50);
        e.HasIndex(x => x.Number);
        e.HasIndex(x => new { x.Year, x.Month });
        e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PayslipConfiguration : IEntityTypeConfiguration<Payslip>
{
    public void Configure(EntityTypeBuilder<Payslip> e)
    {
        e.Property(x => x.EmployeeCode).HasMaxLength(20);
        e.Property(x => x.EmployeeName).HasMaxLength(100);
        e.Property(x => x.Designation).HasMaxLength(60);
        e.Property(x => x.Department).HasMaxLength(60);
        e.Property(x => x.Cnic).HasMaxLength(30);
        e.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(6);
        e.Property(x => x.BankName).HasMaxLength(60);
        e.Property(x => x.Iban).HasMaxLength(34);
        e.Property(x => x.DaysPresent).HasPrecision(7, 2);
        e.Property(x => x.PaidLeaveDays).HasPrecision(7, 2);
        e.Property(x => x.UnpaidLeaveDays).HasPrecision(7, 2);
        e.Property(x => x.OvertimeHours).HasPrecision(9, 2);
        e.Property(x => x.TaxableBefore).HasPrecision(14, 2);
        e.Property(x => x.TaxBefore).HasPrecision(14, 2);
        e.Property(x => x.ProjectedAnnualTaxable).HasPrecision(14, 2);
        e.Property(x => x.ProjectedAnnualTax).HasPrecision(14, 2);
        e.HasIndex(x => new { x.PayrollRunId, x.EmployeeId }).IsUnique();
        e.HasIndex(x => x.EmployeeId);
        e.HasOne<PayrollRun>().WithMany().HasForeignKey(x => x.PayrollRunId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PayslipId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PayslipLineConfiguration : IEntityTypeConfiguration<PayslipLine>
{
    public void Configure(EntityTypeBuilder<PayslipLine> e)
    {
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(22);
        e.Property(x => x.Code).HasMaxLength(20);
        e.Property(x => x.Name).HasMaxLength(80);
        e.Property(x => x.Quantity).HasPrecision(9, 2);
        e.Property(x => x.Rate).HasPrecision(12, 4);
        e.Property(x => x.AccountCode).HasMaxLength(20);
    }
}
