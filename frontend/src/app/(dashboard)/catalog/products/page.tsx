'use client';

import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import apiClient from '@/lib/api-client';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { formatCurrency } from '@/lib/utils';
import { Plus, Search, Pencil } from 'lucide-react';
import type { Product } from '@/types';
import { toast } from '@/components/ui/toaster';

export default function ProductsPage() {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Product | null>(null);
  const [form, setForm] = useState({
    stockCode: '',
    name: '',
    regularPrice: 0,
    tax1Applies: true,
    tax2Applies: true,
  });

  const { data: products = [], isLoading } = useQuery({
    queryKey: ['products', search],
    queryFn: () => apiClient.get<Product[]>(`/products?search=${search}`).then((r) => r.data),
  });

  const createMutation = useMutation({
    mutationFn: (data: typeof form) =>
      apiClient.post('/products', {
        locationId: '00000000-0000-0000-0000-000000000001',
        ...data,
        type: 'Standard',
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['products'] });
      setShowForm(false);
      resetForm();
      toast({ title: 'Product created' });
    },
    onError: (err: any) => {
      toast({ title: 'Error', description: err.response?.data?.error ?? 'Failed', variant: 'destructive' });
    },
  });

  const resetForm = () => setForm({ stockCode: '', name: '', regularPrice: 0, tax1Applies: true, tax2Applies: true });

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Products</h1>
        <Button onClick={() => { setShowForm(true); setEditing(null); resetForm(); }}>
          <Plus className="h-4 w-4 mr-2" /> Add Product
        </Button>
      </div>

      <div className="flex items-center gap-2">
        <Search className="h-5 w-5 text-muted-foreground" />
        <Input
          placeholder="Search products..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="max-w-sm"
        />
      </div>

      {showForm && (
        <Card>
          <CardHeader>
            <CardTitle>{editing ? 'Edit Product' : 'New Product'}</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="grid grid-cols-2 gap-4">
              <Input
                placeholder="Stock Code"
                value={form.stockCode}
                onChange={(e) => setForm({ ...form, stockCode: e.target.value })}
              />
              <Input
                placeholder="Name"
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
              />
              <Input
                type="number"
                placeholder="Regular Price"
                value={form.regularPrice}
                onChange={(e) => setForm({ ...form, regularPrice: parseFloat(e.target.value) || 0 })}
              />
              <div className="flex items-center gap-4">
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={form.tax1Applies}
                    onChange={(e) => setForm({ ...form, tax1Applies: e.target.checked })}
                  />
                  Tax 1
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={form.tax2Applies}
                    onChange={(e) => setForm({ ...form, tax2Applies: e.target.checked })}
                  />
                  Tax 2
                </label>
              </div>
            </div>
            <div className="flex gap-2 mt-4">
              <Button onClick={() => createMutation.mutate(form)} disabled={createMutation.isPending}>
                {createMutation.isPending ? 'Saving...' : 'Save'}
              </Button>
              <Button variant="outline" onClick={() => { setShowForm(false); setEditing(null); }}>
                Cancel
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      <div className="border rounded-lg">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b bg-muted/50">
              <th className="text-left p-3 font-medium">Stock Code</th>
              <th className="text-left p-3 font-medium">Name</th>
              <th className="text-right p-3 font-medium">Price</th>
              <th className="text-right p-3 font-medium">Cost</th>
              <th className="p-3 font-medium"></th>
            </tr>
          </thead>
          <tbody>
            {isLoading ? (
              <tr><td colSpan={5} className="p-8 text-center text-muted-foreground">Loading...</td></tr>
            ) : products.length === 0 ? (
              <tr><td colSpan={5} className="p-8 text-center text-muted-foreground">No products found</td></tr>
            ) : (
              products.map((p) => (
                <tr key={p.id} className="border-b last:border-0 hover:bg-muted/30">
                  <td className="p-3 font-mono text-xs">{p.stockCode}</td>
                  <td className="p-3">{p.name}</td>
                  <td className="p-3 text-right">{formatCurrency(p.regularPrice)}</td>
                  <td className="p-3 text-right">{formatCurrency(p.lastCost)}</td>
                  <td className="p-3 text-right">
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => {
                        setEditing(p);
                        setForm({
                          stockCode: p.stockCode,
                          name: p.name,
                          regularPrice: p.regularPrice,
                          tax1Applies: p.tax1Applies,
                          tax2Applies: p.tax2Applies,
                        });
                        setShowForm(true);
                      }}
                    >
                      <Pencil className="h-4 w-4" />
                    </Button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
