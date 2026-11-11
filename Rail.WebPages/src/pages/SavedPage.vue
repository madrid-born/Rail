<script setup lang="ts">
import { RouterLink } from 'vue-router';
import PageHeading from '../components/PageHeading.vue';
import EmptyState from '../components/EmptyState.vue';
</script>

<template>
  <PageHeading title="ذخیره‌شده‌ها" eyebrow="همیشه در دسترس" description="جای ایستگاه‌ها و مسیرهای مورد علاقهٔ شما" />
  <EmptyState icon="saved" title="هنوز چیزی ذخیره نشده" description="در نسخه‌های بعد می‌توانید ایستگاه‌ها و مسیرهای دلخواهتان را اینجا نگه دارید.">
    <RouterLink class="button" to="/stations">رفتن به ایستگاه‌ها</RouterLink>
  </EmptyState>
</template>
