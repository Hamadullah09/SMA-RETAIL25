'use client';

import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Truck, FileText } from 'lucide-react';

export default function PurchasingPage() {
  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Purchasing</h1>
        <Button>New Purchase Order</Button>
      </div>

      <div className="grid gap-4 md:grid-cols-3">
        <Card>
          <CardHeader>
            <CardTitle className="text-base flex items-center gap-2">
              <Truck className="h-5 w-5" /> Suppliers
            </CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-muted-foreground text-sm">Manage suppliers and their product catalogues.</p>
            <Button variant="outline" className="mt-4" size="sm">View Suppliers</Button>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="text-base flex items-center gap-2">
              <FileText className="h-5 w-5" /> Purchase Orders
            </CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-muted-foreground text-sm">Create and manage purchase orders for stock replenishment.</p>
            <Button variant="outline" className="mt-4" size="sm">View Orders</Button>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="text-base flex items-center gap-2">
              <FileText className="h-5 w-5" /> Goods Received
            </CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-muted-foreground text-sm">Record goods received against purchase orders.</p>
            <Button variant="outline" className="mt-4" size="sm">View Receipts</Button>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
