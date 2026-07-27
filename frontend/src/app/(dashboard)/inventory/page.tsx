'use client';

import { useQuery } from '@tanstack/react-query';
import apiClient from '@/lib/api-client';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Search, Package } from 'lucide-react';
import { formatCurrency } from '@/lib/utils';
import type { StockLevel } from '@/types';

export default function InventoryPage() {
  const { data: stockLevels = [], isLoading } = useQuery({
    queryKey: ['stockLevels'],
    queryFn: () => apiClient.get<StockLevel[]>('/inventory/stock-levels').then((r) => r.data).catch(() => []),
  });

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Inventory</h1>
        <div className="flex gap-2">
          <Button variant="outline">Stock Count</Button>
          <Button variant="outline">Transfer</Button>
          <Button variant="outline">Adjustment</Button>
        </div>
      </div>

      <div className="flex items-center gap-2">
        <Search className="h-5 w-5 text-muted-foreground" />
        <Input placeholder="Search inventory..." className="max-w-sm" />
      </div>

      <div className="border rounded-lg">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b bg-muted/50">
              <th className="text-left p-3 font-medium">Product</th>
              <th className="text-right p-3 font-medium">On Hand</th>
              <th className="text-right p-3 font-medium">Reserved</th>
              <th className="text-right p-3 font-medium">Available</th>
              <th className="text-right p-3 font-medium">Reorder Point</th>
              <th className="text-right p-3 font-medium">Reorder Qty</th>
            </tr>
          </thead>
          <tbody>
            {isLoading ? (
              <tr><td colSpan={6} className="p-8 text-center text-muted-foreground">Loading...</td></tr>
            ) : stockLevels.length === 0 ? (
              <tr><td colSpan={6} className="p-8 text-center text-muted-foreground">
                <Package className="h-8 w-8 mx-auto mb-2 text-muted-foreground" />
                No stock data available
              </td></tr>
            ) : (
              stockLevels.map((s) => (
                <tr key={s.id} className="border-b last:border-0 hover:bg-muted/30">
                  <td className="p-3">{s.productName}</td>
                  <td className="p-3 text-right">{s.onHand}</td>
                  <td className="p-3 text-right">{s.reserved}</td>
                  <td className="p-3 text-right font-medium">{s.available}</td>
                  <td className="p-3 text-right">{s.reorderPoint}</td>
                  <td className="p-3 text-right">{s.reorderQuantity}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
