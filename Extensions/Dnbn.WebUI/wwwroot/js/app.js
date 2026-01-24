// SSE接続管理
let eventSource = null;
let isAutoUpdateEnabled = true;

// 初期化
document.addEventListener('DOMContentLoaded', () => {
  initializeEventSource();
  setupEventHandlers();
  loadInitialStatus();
});

// EventSourceの初期化
function initializeEventSource() {
  if (eventSource) {
    eventSource.close();
  }

  eventSource = new EventSource('/api/status/stream');

  eventSource.onmessage = (event) => {
    try {
      const data = JSON.parse(event.data);
      updateUI(data);
    } catch (error) {
      console.error('SSEメッセージの解析エラー:', error);
    }
  };

  eventSource.onerror = (error) => {
    console.error('SSE接続エラー:', error);
    // EventSourceは自動的に再接続を試みる
  };

  eventSource.addEventListener('connected', (event) => {
    console.log('SSE接続が確立されました');
  });
}

// イベントハンドラの設定
function setupEventHandlers() {
  // リフレッシュボタン
  document.getElementById('refreshBtn').addEventListener('click', () => {
    loadInitialStatus();
  });

  // モーダル閉じるボタン
  document.getElementById('clientModalClose').addEventListener('click', () => {
    document.getElementById('clientModal').classList.add('hidden');
  });

  document.getElementById('serverModalClose').addEventListener('click', () => {
    document.getElementById('serverModal').classList.add('hidden');
  });

  // モーダル背景クリックで閉じる
  document.getElementById('clientModal').addEventListener('click', (e) => {
    if (e.target.id === 'clientModal') {
      document.getElementById('clientModal').classList.add('hidden');
    }
  });

  document.getElementById('serverModal').addEventListener('click', (e) => {
    if (e.target.id === 'serverModal') {
      document.getElementById('serverModal').classList.add('hidden');
    }
  });
}

// 初期状態を読み込む
async function loadInitialStatus() {
  try {
    const response = await fetch('/api/status');
    const data = await response.json();
    updateUI(data);
  } catch (error) {
    console.error('状態の取得エラー:', error);
  }
}

// UIを更新
function updateUI(data) {
  if (data.clients) {
    updateClientsTable(data.clients);
  }
  if (data.servers) {
    updateServersTable(data.servers);
  }
}

// クライアントテーブルを更新
function updateClientsTable(clients) {
  const tbody = document.getElementById('clientsTableBody');
  tbody.innerHTML = '';

  if (clients.length === 0) {
    const row = document.createElement('tr');
    row.innerHTML = '<td colspan="7" class="px-6 py-4 text-center" style="color: #666;">[NO CLIENTS]</td>';
    tbody.appendChild(row);
    return;
  }

  clients.forEach((client, index) => {
    const row = document.createElement('tr');
    row.className = 'transition-all cursor-pointer';
    row.style.cssText = 'transition: all 0.2s;';
    row.addEventListener('click', () => showClientDetail(client));
    row.addEventListener('mouseenter', () => {
      row.style.background = '#f9f9f9';
    });
    row.addEventListener('mouseleave', () => {
      row.style.background = '';
    });

    const statusBadge = client.isConnected
      ? '<span class="status-online font-bold">[ONLINE]</span>'
      : '<span class="status-offline font-bold">[OFFLINE]</span>';

    const remoteAddr = client.remoteHost && client.remotePort
      ? `${client.remoteHost}:${client.remotePort}`
      : '--';

    const uptime = client.connectionDuration || '--:--:--';
    const sentRecv = `${client.messagesSent || 0}/${client.messagesReceived || 0}`;
    const keepAliveTimeout = client.keepAlive?.timeoutCount || 0;
    const errorCount = client.error?.count || 0;
    
    // KeepAlive列の表示: タイムアウト回数と、最後の送信時刻があるかどうかで有効性を判断
    const keepAliveDisplay = keepAliveTimeout > 0 
      ? `<span class="status-offline font-semibold" title="キープアライブタイムアウトが${keepAliveTimeout}回発生">${keepAliveTimeout}</span>`
      : (client.keepAlive?.lastSentAt 
          ? `<span class="status-online" title="キープアライブ正常（タイムアウトなし）">0</span>`
          : `<span style="color: #666;" title="キープアライブ未設定または未送信">--</span>`);
    
    // Errors列の表示: エラーがある場合は強調表示
    const errorDisplay = errorCount > 0
      ? `<span class="status-offline font-semibold" title="エラーが${errorCount}回発生">${errorCount}</span>`
      : `<span class="status-online" title="エラーなし（正常）">0</span>`;

    row.innerHTML = `
      <td class="px-6 py-4 whitespace-nowrap font-medium">${escapeHtml(client.name)}</td>
      <td class="px-6 py-4 whitespace-nowrap">${statusBadge}</td>
      <td class="px-6 py-4 whitespace-nowrap">${escapeHtml(remoteAddr)}</td>
      <td class="px-6 py-4 whitespace-nowrap">${escapeHtml(uptime)}</td>
      <td class="px-6 py-4 whitespace-nowrap">${sentRecv}</td>
      <td class="px-6 py-4 whitespace-nowrap">${keepAliveDisplay}</td>
      <td class="px-6 py-4 whitespace-nowrap">${errorDisplay}</td>
    `;

    tbody.appendChild(row);
  });
}

// サーバーテーブルを更新
function updateServersTable(servers) {
  const tbody = document.getElementById('serversTableBody');
  tbody.innerHTML = '';

  if (servers.length === 0) {
    const row = document.createElement('tr');
    row.innerHTML = '<td colspan="7" class="px-6 py-4 text-center" style="color: #666;">[NO SERVERS]</td>';
    tbody.appendChild(row);
    return;
  }

  servers.forEach((server) => {
    const row = document.createElement('tr');
    row.className = 'transition-all cursor-pointer';
    row.style.cssText = 'transition: all 0.2s;';
    row.addEventListener('click', () => showServerDetail(server));
    row.addEventListener('mouseenter', () => {
      row.style.background = '#f9f9f9';
    });
    row.addEventListener('mouseleave', () => {
      row.style.background = '';
    });

    const statusBadge = server.isRunning
      ? '<span class="status-online font-bold">[RUNNING]</span>'
      : '<span class="status-offline font-bold">[STOPPED]</span>';

    const uptime = server.uptime || '--:--:--';
    const connectionCount = server.connectionCount || 0;
    const totalConnections = server.totalConnections || 0;
    const sentRecv = `${server.messagesSent || 0}/${server.messagesReceived || 0}`;

    row.innerHTML = `
      <td class="px-6 py-4 whitespace-nowrap font-medium">${escapeHtml(server.name)}</td>
      <td class="px-6 py-4 whitespace-nowrap">${statusBadge}</td>
      <td class="px-6 py-4 whitespace-nowrap">${server.listenPort || '--'}</td>
      <td class="px-6 py-4 whitespace-nowrap">${escapeHtml(uptime)}</td>
      <td class="px-6 py-4 whitespace-nowrap font-semibold">${connectionCount}</td>
      <td class="px-6 py-4 whitespace-nowrap">${totalConnections}</td>
      <td class="px-6 py-4 whitespace-nowrap">${sentRecv}</td>
    `;

    tbody.appendChild(row);
  });
}

// クライアント詳細を表示
function showClientDetail(client) {
  const modal = document.getElementById('clientModal');
  const title = document.getElementById('clientModalTitle');
  const content = document.getElementById('clientModalContent');

  title.textContent = `CLIENT: ${escapeHtml(client.name)}`;

  const statusColor = client.isConnected ? '#00aa00' : '#cc0000';
  const statusText = client.isConnected ? '[ONLINE]' : '[OFFLINE]';

  const html = `
    <div class="space-y-6">
      <div class="grid grid-cols-2 gap-4">
        <div>
          <label class="text-xs font-medium" style="color: #666;">[CONNECTION STATUS]</label>
          <p class="mt-1 font-semibold" style="color: ${statusColor};">${statusText}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[REMOTE ADDRESS]</label>
          <p class="mt-1">${escapeHtml(client.remoteHost && client.remotePort ? `${client.remoteHost}:${client.remotePort}` : '--')}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[CONNECTED AT]</label>
          <p class="mt-1">${client.connectedAt ? new Date(client.connectedAt).toLocaleString('ja-JP') : '--'}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[UPTIME]</label>
          <p class="mt-1">${escapeHtml(client.connectionDuration || '--')}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[MESSAGES SENT]</label>
          <p class="mt-1 font-semibold">${client.messagesSent || 0}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[MESSAGES RECEIVED]</label>
          <p class="mt-1 font-semibold">${client.messagesReceived || 0}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[PENDING REQUESTS]</label>
          <p class="mt-1">${client.pendingRequests || 0}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[RECONNECTING]</label>
          <p class="mt-1">${client.isReconnecting ? 'YES' : 'NO'}</p>
        </div>
      </div>

      <div style="border-top: 1px solid #cccccc; padding-top: 1rem;">
        <h4 class="text-base font-semibold mb-3">[KEEPALIVE]</h4>
        <div class="grid grid-cols-2 gap-4">
          <div>
            <label class="text-xs font-medium" style="color: #666;">[LAST SENT]</label>
            <p class="mt-1">${client.keepAlive?.lastSentAt ? new Date(client.keepAlive.lastSentAt).toLocaleString('ja-JP') : '--'}</p>
          </div>
          <div>
            <label class="text-xs font-medium" style="color: #666;">[LAST RESPONSE]</label>
            <p class="mt-1">${client.keepAlive?.lastResponseReceivedAt ? new Date(client.keepAlive.lastResponseReceivedAt).toLocaleString('ja-JP') : '--'}</p>
          </div>
          <div>
            <label class="text-xs font-medium" style="color: #666;">[TIMEOUT COUNT]</label>
            <p class="mt-1" style="color: ${client.keepAlive?.timeoutCount > 0 ? '#cc0000' : '#00aa00'};">${client.keepAlive?.timeoutCount || 0}</p>
          </div>
        </div>
      </div>

      <div style="border-top: 1px solid #cccccc; padding-top: 1rem;">
        <h4 class="text-base font-semibold mb-3">[ERROR INFO]</h4>
        <div class="grid grid-cols-2 gap-4">
          <div>
            <label class="text-xs font-medium" style="color: #666;">[ERROR COUNT]</label>
            <p class="mt-1" style="color: ${client.error?.count > 0 ? '#cc0000' : '#00aa00'};">${client.error?.count || 0}</p>
          </div>
          <div>
            <label class="text-xs font-medium" style="color: #666;">[LAST ERROR AT]</label>
            <p class="mt-1">${client.error?.lastErrorAt ? new Date(client.error.lastErrorAt).toLocaleString('ja-JP') : '--'}</p>
          </div>
          <div class="col-span-2">
            <label class="text-xs font-medium" style="color: #666;">[LAST ERROR MESSAGE]</label>
            <p class="mt-1 break-words" style="color: ${client.error?.lastError ? '#cc0000' : '#333'}; font-family: monospace;">${escapeHtml(client.error?.lastError || '--')}</p>
          </div>
        </div>
      </div>

      <div style="border-top: 1px solid #cccccc; padding-top: 1rem;">
        <h4 class="text-base font-semibold mb-3">[CONNECTION RETRY]</h4>
        <div class="grid grid-cols-2 gap-4">
          <div>
            <label class="text-xs font-medium" style="color: #666;">[RETRY ATTEMPTS]</label>
            <p class="mt-1">${client.connectionRetry?.attempts || 0}</p>
          </div>
          <div>
            <label class="text-xs font-medium" style="color: #666;">[LAST ATTEMPT AT]</label>
            <p class="mt-1">${client.connectionRetry?.lastAttemptAt ? new Date(client.connectionRetry.lastAttemptAt).toLocaleString('ja-JP') : '--'}</p>
          </div>
        </div>
      </div>
    </div>
  `;

  content.innerHTML = html;
  modal.classList.remove('hidden');
}

// サーバー詳細を表示
function showServerDetail(server) {
  const modal = document.getElementById('serverModal');
  const title = document.getElementById('serverModalTitle');
  const content = document.getElementById('serverModalContent');

  title.textContent = `SERVER: ${escapeHtml(server.name)}`;

  const statusColor = server.isRunning ? '#00aa00' : '#cc0000';
  const statusText = server.isRunning ? '[RUNNING]' : '[STOPPED]';

  const sessionsHtml = server.sessions && server.sessions.length > 0
    ? `
      <div class="mt-4 overflow-x-auto">
        <table class="min-w-full" style="border: 1px solid #cccccc;">
          <thead style="background: #f5f5f5;">
            <tr>
              <th class="px-4 py-3 text-left text-xs font-medium uppercase font-mono">SESSION ID</th>
              <th class="px-4 py-3 text-left text-xs font-medium uppercase font-mono">SOURCE ENDPOINT</th>
              <th class="px-4 py-3 text-left text-xs font-medium uppercase font-mono">CONNECTED AT</th>
              <th class="px-4 py-3 text-left text-xs font-medium uppercase font-mono">LAST MESSAGE</th>
              <th class="px-4 py-3 text-left text-xs font-medium uppercase font-mono">ACTIVE</th>
            </tr>
          </thead>
          <tbody>
            ${server.sessions.map(s => `
              <tr style="border-bottom: 1px solid #eeeeee;">
                <td class="px-4 py-3 whitespace-nowrap text-sm font-mono">${escapeHtml(s.sessionId)}</td>
                <td class="px-4 py-3 whitespace-nowrap text-sm font-mono">${escapeHtml(s.sourceEndpoint)}</td>
                <td class="px-4 py-3 whitespace-nowrap text-sm font-mono">${s.connectedAt ? new Date(s.connectedAt).toLocaleString('ja-JP') : '--'}</td>
                <td class="px-4 py-3 whitespace-nowrap text-sm font-mono">${s.lastMessageReceivedAt ? new Date(s.lastMessageReceivedAt).toLocaleString('ja-JP') : '--'}</td>
                <td class="px-4 py-3 whitespace-nowrap text-sm font-mono">${s.isActive ? '<span class="status-online">[ACTIVE]</span>' : '<span style="color: #666;">[INACTIVE]</span>'}</td>
              </tr>
            `).join('')}
          </tbody>
        </table>
      </div>
    `
    : '<p class="text-sm font-mono" style="color: #666;">[NO SESSIONS]</p>';

  const html = `
    <div class="space-y-6">
      <div class="grid grid-cols-2 gap-4">
        <div>
          <label class="text-xs font-medium" style="color: #666;">[STATUS]</label>
          <p class="mt-1 font-semibold" style="color: ${statusColor};">${statusText}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[LISTEN PORT]</label>
          <p class="mt-1">${server.listenPort || '--'}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[STARTED AT]</label>
          <p class="mt-1">${server.startedAt ? new Date(server.startedAt).toLocaleString('ja-JP') : '--'}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[UPTIME]</label>
          <p class="mt-1">${escapeHtml(server.uptime || '--')}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[CURRENT CONNECTIONS]</label>
          <p class="mt-1 font-semibold">${server.connectionCount || 0}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[TOTAL CONNECTIONS]</label>
          <p class="mt-1">${server.totalConnections || 0}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[MESSAGES SENT]</label>
          <p class="mt-1 font-semibold">${server.messagesSent || 0}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[MESSAGES RECEIVED]</label>
          <p class="mt-1 font-semibold">${server.messagesReceived || 0}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[LAST CLIENT CONNECTED]</label>
          <p class="mt-1">${server.lastClientConnectedAt ? new Date(server.lastClientConnectedAt).toLocaleString('ja-JP') : '--'}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[LAST CLIENT DISCONNECTED]</label>
          <p class="mt-1">${server.lastClientDisconnectedAt ? new Date(server.lastClientDisconnectedAt).toLocaleString('ja-JP') : '--'}</p>
        </div>
      </div>

      <div style="border-top: 1px solid #cccccc; padding-top: 1rem;">
        <h4 class="text-base font-semibold mb-3 font-mono">[SESSIONS] (${server.sessions?.length || 0})</h4>
        ${sessionsHtml}
      </div>
    </div>
  `;

  content.innerHTML = html;
  modal.classList.remove('hidden');
}

// HTMLエスケープ
function escapeHtml(text) {
  if (text == null) return '--';
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}
