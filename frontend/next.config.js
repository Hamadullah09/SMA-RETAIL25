/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,

  // Emits .next/standalone/server.js with only the modules the app actually reaches. Shared
  // hosting is the reason it is not optional: a full node_modules tree is tens of thousands of
  // files, and uploading that over the control panel's file manager is measured in hours.
  output: 'standalone',
};

module.exports = nextConfig;
