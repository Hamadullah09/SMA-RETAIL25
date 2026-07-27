'use client';

import { useQuery } from '@tanstack/react-query';
import apiClient from '@/lib/api-client';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { ShoppingCart, Users, Package, TrendingUp } from 'lucide-react';
import { formatCurrency } from '@/lib/utils';

export default function DashboardPage() {
  const { data: products } = useQuery({
    queryKey: ['products'],
    queryFn: () => apiClient.get('/products?search=').then((r) => r.data),
  });

  const { data: customers } = useQuery({
    queryKey: ['customers'],
    queryFn: () => apiClient.get('/customers?search=').then((r) => r.data),
  });

  const stats = [
    { title: 'Total Products', value: products?.length ?? 0, icon: Package, color: 'text-blue-600' },
    { title: 'Total Customers', value: customers?.length ?? 0, icon: Users, color: 'text-green-600' },
    { title: 'Sales Today', value: formatCurrency(0), icon: TrendingUp, color: 'text-orange-600' },
    { title: 'Open Carts', value: 0, icon: ShoppingCart, color: 'text-purple-600' },
  ];

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-bold">Dashboard</h1>
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
        {stats.map((stat) => (
          <Card key={stat.title}>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium">{stat.title}</CardTitle>
              <stat.icon className={`h-5 w-5 ${stat.color}`} />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-bold">{stat.value}</div>
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
}
