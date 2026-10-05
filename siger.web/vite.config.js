import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-react';

// https://vitejs.dev/config/
export default defineConfig({
    plugins: [plugin()],
    server: {
        port: 49338,
        strictPort: true,
    },
    test: {
        environment: 'jsdom',
        setupFiles: './src/test/setup.js',
    },
})
