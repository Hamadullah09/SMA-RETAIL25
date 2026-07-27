'use client';

import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { BarChart3, FileText, TrendingUp, Users } from 'lucide-react';

const reports = [
  { title: 'Sales Summary', description: 'Daily, weekly, and monthly sales summaries', icon: TrendingUp },
  { title: 'Sales by Department', description: 'Sales breakdown by department and category', icon: BarChart3 },
  { title: 'Top Products', description: 'Best-selling products by volume and revenue', icon: FileText },
  { title: 'Customer Statements', description: 'Account statements for credit customers', icon: Users },
  { title: 'Tax Report', description: 'Tax collected and payable summary', icon: FileText },
  { title: 'Stock Valuation', description: 'Current stock levels and valuation', icon: FileText },
];

export default function ReportsPage() {
  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-bold">Reports</h1>

      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        {reports.map((report) => (
          <Card key={report.title} className="hover:shadow-md transition-shadow cursor-pointer">
            <CardHeader>
              <CardTitle className="text-base flex items-center gap-2">
                <report.icon className="h-5 w-5 text-primary" />
                {report.title}
              </CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-sm text-muted-foreground mb-4">{report.description}</p>
              <Button variant="outline" size="sm">Generate Report</Button>
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
}
