'use client';

import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Users, MapPin, Building2, Settings, Shield } from 'lucide-react';

const adminSections = [
  { title: 'Staff Management', description: 'Manage staff accounts, roles, and permissions', icon: Users },
  { title: 'Locations', description: 'Configure store locations and terminals', icon: MapPin },
  { title: 'Departments', description: 'Manage product departments and categories', icon: Building2 },
  { title: 'Tax Configuration', description: 'Configure tax rates and rules', icon: Settings },
  { title: 'Security', description: 'User roles, permissions, and access control', icon: Shield },
];

export default function AdminPage() {
  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-bold">Administration</h1>

      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        {adminSections.map((section) => (
          <Card key={section.title} className="hover:shadow-md transition-shadow cursor-pointer">
            <CardHeader>
              <CardTitle className="text-base flex items-center gap-2">
                <section.icon className="h-5 w-5 text-primary" />
                {section.title}
              </CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-sm text-muted-foreground mb-4">{section.description}</p>
              <Button variant="outline" size="sm">Configure</Button>
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
}
