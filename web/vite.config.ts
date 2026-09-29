import { defineConfig } from 'vite';

export default defineConfig({
  server: {
    // The Server's port, from src/MetroDisplay.Server/Properties/launchSettings.json.
    proxy: { '/api': 'http://localhost:5180' },
  },
});
