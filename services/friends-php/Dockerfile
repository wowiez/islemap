FROM composer:2 AS composer
FROM php:8.3-cli-bookworm
RUN apt-get update && apt-get install -y --no-install-recommends libicu-dev libonig-dev unzip \
    && docker-php-ext-install intl mbstring pcntl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=composer /usr/bin/composer /usr/local/bin/composer
WORKDIR /app
COPY composer.json composer.lock ./
RUN composer install --no-dev --no-interaction --prefer-dist --no-progress
COPY src ./src
COPY server.php import.php verify-import.php tests.php ./
ENV ISLEMAP_DB=/data/islemap.sqlite
EXPOSE 8080 8081
CMD ["php", "server.php", "start"]
