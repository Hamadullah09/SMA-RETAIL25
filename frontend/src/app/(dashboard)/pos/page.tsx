'use client';

import { useState, useRef, useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import apiClient from '@/lib/api-client';
import { useCartStore } from '@/stores/cart-store';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { formatCurrency } from '@/lib/utils';
import { Trash2, ShoppingCart, Search, X } from 'lucide-react';
import type { Product } from '@/types';
import { toast } from '@/components/ui/toaster';

export default function POSPage() {
  const { cart, createCart, addItem, removeItem, isLoading, error, clearError } = useCartStore();
  const [searchTerm, setSearchTerm] = useState('');
  const [searchResults, setSearchResults] = useState<Product[]>([]);
  const [showSearch, setShowSearch] = useState(false);
  const scanRef = useRef<HTMLInputElement>(null);
  const searchTimer = useRef<NodeJS.Timeout>();

  useEffect(() => {
    scanRef.current?.focus();
  }, []);

  const handleScan = (identifier: string) => {
    if (!identifier.trim()) return;
    if (!cart) {
      createCart('00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000001')
        .then(() => addItem(identifier))
        .catch(() => {});
    } else {
      addItem(identifier);
    }
    setSearchTerm('');
    scanRef.current?.focus();
  };

  const handleSearch = (term: string) => {
    setSearchTerm(term);
    if (searchTimer.current) clearTimeout(searchTimer.current);
    if (term.length < 2) {
      setSearchResults([]);
      setShowSearch(false);
      return;
    }
    searchTimer.current = setTimeout(async () => {
      try {
        const { data } = await apiClient.get(`/products?search=${encodeURIComponent(term)}`);
        setSearchResults(data);
        setShowSearch(true);
      } catch {
        setSearchResults([]);
      }
    }, 300);
  };

  return (
    <div className="pos-grid gap-4 h-[calc(100vh-7rem)]">
      {/* Left: Product Grid & Search */}
      <div className="flex flex-col h-full">
        <Card className="flex-1 flex flex-col overflow-hidden">
          <CardHeader className="pb-3">
            <div className="flex items-center gap-2">
              <Search className="h-5 w-5 text-muted-foreground" />
              <Input
                ref={scanRef}
                placeholder="Scan barcode or search products..."
                value={searchTerm}
                onChange={(e) => handleSearch(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') handleScan(searchTerm);
                  if (e.key === 'Escape') {
                    setSearchTerm('');
                    setShowSearch(false);
                  }
                }}
                className="flex-1"
              />
            </div>
          </CardHeader>
          <CardContent className="flex-1 overflow-y-auto">
            {showSearch && searchResults.length > 0 ? (
              <div className="grid gap-2">
                {searchResults.map((product) => (
                  <button
                    key={product.id}
                    onClick={() => {
                      handleScan(product.stockCode);
                      setShowSearch(false);
                    }}
                    className="flex items-center justify-between p-3 rounded-lg border hover:bg-accent text-left"
                  >
                    <div>
                      <p className="font-medium">{product.name}</p>
                      <p className="text-sm text-muted-foreground">{product.stockCode}</p>
                    </div>
                    <span className="font-bold">{formatCurrency(product.regularPrice)}</span>
                  </button>
                ))}
              </div>
            ) : showSearch ? (
              <p className="text-center text-muted-foreground py-8">No products found</p>
            ) : (
              <p className="text-center text-muted-foreground py-8">Scan a barcode or type to search</p>
            )}
          </CardContent>
        </Card>

        {error && (
          <div className="mt-2 p-3 bg-destructive/10 border border-destructive/20 rounded-md flex items-center justify-between">
            <p className="text-sm text-destructive">{error}</p>
            <Button variant="ghost" size="sm" onClick={clearError}>
              <X className="h-4 w-4" />
            </Button>
          </div>
        )}
      </div>

      {/* Right: Cart */}
      <Card className="flex flex-col h-full overflow-hidden">
        <CardHeader className="pb-3">
          <div className="flex items-center justify-between">
            <CardTitle className="flex items-center gap-2">
              <ShoppingCart className="h-5 w-5" />
              Current Sale
            </CardTitle>
            {cart && (
              <span className="text-sm text-muted-foreground">{cart.itemCount} items</span>
            )}
          </div>
        </CardHeader>
        <CardContent className="flex-1 overflow-y-auto p-0">
          {!cart || cart.lines.length === 0 ? (
            <p className="text-center text-muted-foreground py-12">No items in cart</p>
          ) : (
            <div className="divide-y">
              {cart.lines
                .filter((l) => l.lineType === 'Product')
                .map((line) => (
                  <div key={line.id} className="flex items-center justify-between p-4">
                    <div className="flex-1 min-w-0">
                      <p className="font-medium truncate">{line.description}</p>
                      <p className="text-sm text-muted-foreground">
                        {line.quantity} x {formatCurrency(line.sellingPrice)}
                      </p>
                    </div>
                    <div className="flex items-center gap-2 ml-4">
                      <span className="font-bold">{formatCurrency(line.sellingPrice * line.quantity)}</span>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="h-8 w-8 text-destructive"
                        onClick={() => removeItem(line.id)}
                        disabled={isLoading}
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    </div>
                  </div>
                ))}
            </div>
          )}
        </CardContent>

        {/* Totals */}
        {cart && cart.lines.length > 0 && (
          <div className="border-t p-4 space-y-2">
            <div className="flex justify-between text-sm">
              <span>Subtotal</span>
              <span>{formatCurrency(cart.subtotal)}</span>
            </div>
            {cart.totalDiscount > 0 && (
              <div className="flex justify-between text-sm text-green-600">
                <span>Discount</span>
                <span>-{formatCurrency(cart.totalDiscount)}</span>
              </div>
            )}
            <div className="flex justify-between text-sm">
              <span>Tax</span>
              <span>{formatCurrency(cart.tax1Total + cart.tax2Total)}</span>
            </div>
            <div className="flex justify-between text-lg font-bold border-t pt-2">
              <span>Total</span>
              <span>{formatCurrency(cart.grandTotal)}</span>
            </div>
            <div className="grid grid-cols-2 gap-2 pt-2">
              <Button variant="outline" onClick={() => useCartStore.getState().voidCart()}>
                Void Sale
              </Button>
              <Button disabled={isLoading}>Pay</Button>
            </div>
          </div>
        )}
      </Card>
    </div>
  );
}
