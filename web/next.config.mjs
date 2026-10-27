/**
 * The web back office is a static site: `next build` writes plain files to `out/`, and RetailPlus.Server serves them
 * next to the API. So there are no server components that fetch, no route handlers, no middleware and no dynamic route
 * segments; detail pages read `?id=` on the client.
 *
 * @type {import('next').NextConfig}
 */
const nextConfig = {
  output: 'export',
  trailingSlash: true,
  reactStrictMode: true,
  images: { unoptimized: true },
  poweredByHeader: false,
};

export default nextConfig;
