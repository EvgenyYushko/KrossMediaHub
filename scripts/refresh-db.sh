#!/bin/bash
# Скрипт выполняет следующие шаги:
# 1. Останавливает веб-сервис.
# 2. Создаёт бэкап базы данных.
# 3. Опционально загружает бэкап в Google Drive.
# 4. Пересоздаёт базу данных.
# 5. Ожидает готовность новой базы данных.
# 6. Восстанавливает данные из бэкапа.
# 7. Обновляет переменные окружения (например, DB_URL_POSTGRESQL) через API Render.
# 8. Перезапускает веб-сервис и проверяет его доступность.

# =============================================
# Конфигурация
# =============================================
RENDER_API="https://api.render.com/v1"
BACKUP_FILE_NAME="backup.dump"
NEW_DB_NAME="krosshub"
NEW_DB_USER="krosshub_user"
SITE_URL="https://krossmediahub.onrender.com/"
RENDER_SERVICE_TYPE="postgres"  # Тип сервиса для API Render
MAX_RETRIES=30                  # Максимальное количество попыток проверки доступности сайта
RETRY_INTERVAL=45               # Интервал между проверками сайта (сек)

# =============================================
# Вспомогательные функции
# =============================================

# Логирование с цветами и иконками
log_info() {
    printf "\e[34mℹ %s\e[0m\n" "$1"
}

log_success() {
    printf "\e[32m✔ %s\e[0m\n" "$1"
}

log_warning() {
    printf "\e[33m⚠ %s\e[0m\n" "$1"
}

log_error() {
    printf "\e[31m❌ %s\e[0m\n" "$1" >&2
}

# Вызов API Render.com
render_api_request() {
    local method=$1
    local endpoint=$2
    local data=$3

    curl -sSf -X "$method" \
         -H "accept: application/json" \
         -H "authorization: Bearer $RENDER_API_KEY" \
         -H "content-type: application/json" \
         --data "$data" \
         "${RENDER_API}/${endpoint}"
}

# Обработка ошибок: вывод сообщения, попытка возобновления веб-сервиса и завершение работы
handle_error() {
    log_error "Script failed! Attempting to start the web service..."
    render_api_request "POST" "services/$RENDER_SERVICE_ID/resume" "" > /dev/null
    render_api_request "POST" "services/$RENDER_SERVICE_ID/deploys" "{\"clearCache\":\"do_not_clear\"}" > /dev/null
    exit 1
}
trap 'handle_error' ERR

# Функция ожидания готовности новой базы данных
wait_for_db_ready() {
    log_info "⏳ Ожидание готовности новой базы данных (NEW_DB_ID: $NEW_DB_ID)..."

    for i in $(seq 1 $MAX_RETRIES); do
        CHECK_DB_RESPONSE=$(curl -s --request GET \
                 --url "https://api.render.com/v1/postgres/$NEW_DB_ID" \
                 --header 'accept: application/json' \
                 --header "authorization: Bearer $RENDER_API_KEY")

        log_warning "Ответ от Render API: $CHECK_DB_RESPONSE"  # Логирование полного ответа

        STATUS=$(echo "$CHECK_DB_RESPONSE" | jq -r '.status // empty' 2>/dev/null)

        if [ "$STATUS" == "available" ]; then
            log_success "БД готова! Статус: $STATUS."
            return 0
        fi

        log_info "Статус базы данных: ${STATUS:-неизвестен}. Повтор через $RETRY_INTERVAL секунд..."
        sleep $RETRY_INTERVAL
    done
    log_error "База данных не стала доступной в течение отведённого времени."
    return 1
}

# Функция опциональной загрузки бэкапа в Google Drive с использованием rclone
upload_to_gdrive() {
    log_info "Загрузка бэкапа в Google Drive..."
    if ! rclone copy "$BACKUP_FILE_NAME" "gdrive:$(date +'%Y-%m-%d_%H-%M-%S')/backup.dump" --drive-root-folder-id="$GOOGLE_DRIVE_FOLDER_ID"; then
        log_warning "Не удалось загрузить файл в Google Drive"
        else
        log_success "Бэкапа успешно загружен в Google Drive!"
    fi
}

# =============================================
# Основной скрипт
# =============================================

# Получение информации о существующей БД
log_info "Поиск существующей базы данных TelergamDB..."
DB_ID=$(render_api_request "GET" "${RENDER_SERVICE_TYPE}?includeReplicas=true&limit=20" "" | \
         jq -r '.[] | select(.postgres.name=="TelergamDB") | .postgres.id')

if [ -n "$DB_ID" ] && [ "$DB_ID" != "null" ]; then
    log_success "Найдена база данных TelergamDB (ID: $DB_ID)"
else
    log_error "База данных TelergamDB не найдена"
    exit 1
fi

# Остановка веб-сервиса
log_info "Остановка веб-сервиса..."
render_api_request "POST" "services/$RENDER_SERVICE_ID/suspend" "" > /dev/null

# Создание бэкапа
log_info "Создание бэкапа базы данных TelergamDB..."

DB_INFO=$(render_api_request "GET" "${RENDER_SERVICE_TYPE}/$DB_ID" "")
CONNECTION_INFO=$(render_api_request "GET" "${RENDER_SERVICE_TYPE}/$DB_ID/connection-info" "")

DB_NAME=$(jq -r '.databaseName' <<< "$DB_INFO")
DB_USER_FROM_INFO=$(jq -r '.databaseUser' <<< "$DB_INFO")
PGPASSWORD=$(jq -r '.password' <<< "$CONNECTION_INFO")
DB_HOST="$DB_ID.oregon-postgres.render.com"
DB_PORT=5432  # Порт PostgreSQL по умолчанию

if [ -z "$DB_NAME" ] || [ "$DB_NAME" == "null" ] || 
   [ -z "$DB_USER_FROM_INFO" ] || [ "$DB_USER_FROM_INFO" == "null" ] || 
   [ -z "$PGPASSWORD" ] || [ "$PGPASSWORD" == "null" ]; then
    log_error "Не хватает данных для создания бекапа: $DB_INFO"
    log_error "DB_NAME=$DB_NAME DB_USER_FROM_INFO=$DB_USER_FROM_INFO PGPASSWORD=$PGPASSWORD"
    handle_error
    exit 1
fi

export PGPASSWORD
if ! pg_dump -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER_FROM_INFO" -d "$DB_NAME" --no-owner --no-acl -Fc -f "$BACKUP_FILE_NAME"; then
    log_error "Ошибка при создании бэкапа"
    handle_error
    exit 1
fi
log_success "Бэкап успешно создан: $BACKUP_FILE_NAME"

# Опциональная загрузка бэкапа в Google Drive
upload_to_gdrive || true

render_api_request "POST" "${RENDER_SERVICE_TYPE}/$DB_ID/suspend" ""
render_api_request "DELETE" "${RENDER_SERVICE_TYPE}/$DB_ID" ""

# Пересоздание базы данных
log_info "Cоздание новой базы данных..."
render_api_request "POST" "$RENDER_SERVICE_TYPE" "{
    \"databaseName\": \"$NEW_DB_NAME\",
    \"databaseUser\": \"$NEW_DB_USER\",
    \"enableHighAvailability\": false,
    \"plan\": \"free\",
    \"version\": \"16\",
    \"name\": \"TelergamDB\",
    \"ownerId\": \"tea-ct84bie8ii6s73ccgf1g\",
    \"ipAllowList\": [{\"cidrBlock\": \"0.0.0.0/0\", \"description\": \"everywhere\"}]
}" | jq '.' > response.json

NEW_DB_ID=$(jq -r '.id' response.json)
NEW_DB_NAME=$(jq -r '.databaseName' response.json)
NEW_DB_USER=$(jq -r '.databaseUser' response.json)

if [ -n "$NEW_DB_ID" ] && [ "$NEW_DB_ID" != "null" ]; then
    log_success "Новая база данных создана (NEW_DB_NAME=$NEW_DB_NAME NEW_DB_ID: $NEW_DB_ID)"
else
    log_error "Ошибка при создании БД. Ответ от Render API:"
    jq '.' response.json
    handle_error
    exit 1
fi

# Ожидание готовности новой базы данных
if ! wait_for_db_ready; then
    log_error "База данных не стала доступной. Прерывание восстановления."
    handle_error
    exit 1
fi

# Восстановление данных из бэкапа
log_info "Восстановление данных из бэкапа $BACKUP_FILE_NAME..."
NEW_DB_PASSWORD=$(render_api_request "GET" "${RENDER_SERVICE_TYPE}/$NEW_DB_ID/connection-info" "" | jq -r '.password')
export PGPASSWORD=$NEW_DB_PASSWORD

log_info "⏳ ЖДём 20 секунд..."
sleep 20

#echo "NEW_DB_USER="$NEW_DB_USER "NEW_DB_NAME=" $NEW_DB_NAME "NEW_DB_PASSWORD="$NEW_DB_PASSWORD

if ! pg_restore -h "${NEW_DB_ID}.oregon-postgres.render.com" -p 5432 -U "$NEW_DB_USER" -d "$NEW_DB_NAME" --no-owner "$BACKUP_FILE_NAME"; then
    log_error "Ошибка восстановления данных"
    handle_error
    exit 1
fi

log_success "База данных $NEW_DB_NAME успешно восстановлена! (NEW_DB_ID=$NEW_DB_ID)"

# Обновление переменных окружения (DB_URL_POSTGRESQL)
log_info "Обновление переменных окружения..."
CONNECTION_STRING="Host=$NEW_DB_ID;Database=$NEW_DB_NAME;Username=$NEW_DB_USER;Password=$NEW_DB_PASSWORD;Port=5432;SSL Mode=Require;Trust Server Certificate=true"
render_api_request "PUT" "services/$RENDER_SERVICE_ID/env-vars/DB_URL_POSTGRESQL" "{\"value\":\"$CONNECTION_STRING\"}" > /dev/null
log_success "Переменные окружения обновлены!"

# Перезапуск веб-сервиса
log_info "🔄 Перезапуск веб-сервиса..."
render_api_request "POST" "services/$RENDER_SERVICE_ID/resume" "" > /dev/null
render_api_request "POST" "services/$RENDER_SERVICE_ID/deploys" "{\"clearCache\":\"do_not_clear\"}" > /dev/null
log_success "Веб-сервис запущен!"

# Проверка доступности веб-сервиса
log_info "Проверка доступности веб-сервиса..."
for i in $(seq 1 $MAX_RETRIES); do
    HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$SITE_URL")
    if [ "$HTTP_STATUS" -eq 200 ]; then
        break
    fi
    log_info "⏳ Статус сервиса: $HTTP_STATUS. Повтор через $RETRY_INTERVAL секунд..."
    sleep $RETRY_INTERVAL
done

if [ "$HTTP_STATUS" -eq 200 ]; then
    log_success "Сервис доступен! 🚀🚀🚀"
else
    log_error "Сервис недоступен"
fi

exit 0
