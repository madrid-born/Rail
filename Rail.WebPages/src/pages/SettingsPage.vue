<script setup lang="ts">
import PageHeading from '../components/PageHeading.vue';
import { useTheme } from '../composables/useTheme';

const { theme } = useTheme();
</script>

<template>
  <PageHeading title="تنظیمات" eyebrow="Rail، به سلیقهٔ شما" description="ظاهر برنامه را انتخاب کنید." />
  <section class="card" aria-labelledby="appearance-title">
    <h2 id="appearance-title">تنظیمات برنامه</h2>
    <div class="setting-row">
      <div><label for="theme">ظاهر برنامه</label><p>روشن، تیره یا هماهنگ با دستگاه</p></div>
      <select id="theme" v-model="theme">
        <option value="system">مطابق دستگاه</option>
        <option value="light">روشن</option>
        <option value="dark">تیره</option>
      </select>
    </div>
    <div class="setting-row">
      <div><strong>زبان برنامه</strong><p>فارسی</p></div>
      <span lang="en" class="version">Rail</span>
    </div>
  </section>
</template>
