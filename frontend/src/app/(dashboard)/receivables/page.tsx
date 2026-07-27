'use client';

import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { CreditCard, FileText } from 'lucide-react';
import { Button } from '@/components/ui/button';

export default function ReceivablesPage() {
  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-bold">Accounts Receivable</h1>

      <div className="grid gap-4 md:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="text-base flex items-center gap-2">
              <CreditCard className="h-5 w-5" /> Customer Accounts
            </CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-sm text-muted-foreground mb-4">
              View and manage customer credit accounts, balances, and payment history.
            </p>
            <Button variant="outline" size="sm">View Accounts</Button>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="text-base flex items-center gap-2">
              <FileText className="h-5 w-5" /> Gift Certificates
            </CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-sm text-muted-foreground mb-4">
              Issue, redeem, and manage gift certificates.
            </p>
            <Button variant="outline" size="sm">Manage Gift Certificates</Button>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
