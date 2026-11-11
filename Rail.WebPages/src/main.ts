import { createApp } from 'vue';
import App from './App.vue';
import { router } from './router';
import { initializeTheme } from './composables/useTheme';
import './styles/theme.css';

initializeTheme();
const app = createApp(App);
app.use(router);
router.isReady().then(() => app.mount('#app'));
