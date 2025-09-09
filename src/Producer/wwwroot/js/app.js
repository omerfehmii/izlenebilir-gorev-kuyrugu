// Load task types
fetch('/api/task/types')
    .then(response => response.json())
    .then(taskTypes => {
        const select = document.getElementById('taskType');
        taskTypes.forEach(type => {
            const option = document.createElement('option');
            option.value = type;
            option.textContent = type;
            select.appendChild(option);
        });
    })
    .catch(error => {
        console.error('Error loading task types:', error);
    });

// Load stats
function loadStats() {
    fetch('/api/task/stats')
        .then(response => response.json())
        .then(stats => {
            document.getElementById('tasksSent').textContent = stats.tasksSent;
            document.getElementById('systemStatus').textContent = stats.status;
            document.getElementById('lastUpdate').textContent = new Date(stats.timestamp).toLocaleTimeString('tr-TR');
        })
        .catch(error => {
            console.error('Error loading stats:', error);
            document.getElementById('systemStatus').textContent = 'Error';
        });
}

// Load stats on page load and refresh every 5 seconds
loadStats();
setInterval(loadStats, 5000);

// Form submission
document.getElementById('taskForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    
    const formData = {
        taskType: document.getElementById('taskType').value,
        title: document.getElementById('title').value,
        description: document.getElementById('description').value,
        priority: parseInt(document.getElementById('priority').value) || 5
    };

    const submitBtn = document.getElementById('submitBtn');
    const loading = document.getElementById('loading');
    const statusDiv = document.getElementById('status');

    submitBtn.disabled = true;
    loading.style.display = 'block';
    statusDiv.innerHTML = '';

    try {
        const response = await fetch('/api/task/send', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(formData)
        });

        const result = await response.json();

        if (response.ok) {
            statusDiv.innerHTML = `<div class='status success'>✅ Görev başarıyla gönderildi! ID: ${result.taskId}</div>`;
            document.getElementById('taskForm').reset();
            loadStats(); // Refresh stats
        } else {
            statusDiv.innerHTML = `<div class='status error'>❌ Hata: ${result.message}</div>`;
        }
    } catch (error) {
        statusDiv.innerHTML = `<div class='status error'>❌ Bağlantı hatası: ${error.message}</div>`;
    }

    submitBtn.disabled = false;
    loading.style.display = 'none';
});

// AI-Optimized Auto Task System Controls
let autoTaskInterval = null;
let isAutoTaskRunning = false;

// AI Service Status Check
async function checkAIServiceStatus() {
    try {
        const response = await fetch('/api/ai/health');
        const isHealthy = response.ok;
        
        const aiStatusElement = document.getElementById('aiStatus');
        if (isHealthy) {
            aiStatusElement.textContent = '✅';
            aiStatusElement.className = 'stat-value ai-status-healthy';
        } else {
            aiStatusElement.textContent = '❌';
            aiStatusElement.className = 'stat-value ai-status-unhealthy';
        }
        
        return isHealthy;
    } catch (error) {
        document.getElementById('aiStatus').textContent = '❌';
        document.getElementById('aiStatus').className = 'stat-value ai-status-unhealthy';
        return false;
    }
}

// Auto Task Status Check
async function checkAutoTaskStatus() {
    try {
        const response = await fetch('/api/autotasks/status');
        const status = await response.json();
        
        const autoTaskStatusElement = document.getElementById('autoTaskStatus');
        if (status.isRunning) {
            autoTaskStatusElement.textContent = 'Çalışıyor';
            autoTaskStatusElement.className = 'stat-value auto-task-running';
        } else {
            autoTaskStatusElement.textContent = 'Durduruldu';
            autoTaskStatusElement.className = 'stat-value auto-task-stopped';
        }
        
        return status.isRunning;
    } catch (error) {
        return false;
    }
}

// Start Auto Tasks
document.getElementById('startAutoTasks').addEventListener('click', async () => {
    const interval = document.getElementById('autoTaskInterval').value;
    const scenario = document.getElementById('taskScenario').value;
    
    try {
        const response = await fetch('/api/autotasks/start', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ 
                intervalSeconds: parseInt(interval),
                scenario: scenario 
            })
        });
        
        if (response.ok) {
            let result = {};
            try {
                result = await response.json();
            } catch (e) {
                result = { message: "Started successfully" };
            }
            
            document.getElementById('startAutoTasks').style.display = 'none';
            document.getElementById('stopAutoTasks').style.display = 'inline-block';
            
            document.getElementById('autoTaskStatus').innerHTML = 
                `<div class='status success'>✅ Otomatik görevler başlatıldı! Aralık: ${interval}s, Senaryo: ${scenario}</div>`;
                
            isAutoTaskRunning = true;
        } else {
            let error = { message: "Unknown error" };
            try {
                error = await response.json();
            } catch (e) {
                error = { message: `HTTP ${response.status}` };
            }
            document.getElementById('autoTaskStatus').innerHTML = 
                `<div class='status error'>❌ Başlatma hatası: ${error.message}</div>`;
        }
    } catch (error) {
        document.getElementById('autoTaskStatus').innerHTML = 
            `<div class='status error'>❌ Bağlantı hatası: ${error.message}</div>`;
    }
});

// Stop Auto Tasks
document.getElementById('stopAutoTasks').addEventListener('click', async () => {
    try {
        const response = await fetch('/api/autotasks/stop', { method: 'POST' });
        
        if (response.ok) {
            let result = {};
            try {
                result = await response.json();
            } catch (e) {
                result = { message: "Stopped successfully" };
            }
            
            document.getElementById('startAutoTasks').style.display = 'inline-block';
            document.getElementById('stopAutoTasks').style.display = 'none';
            
            document.getElementById('autoTaskStatus').innerHTML = 
                `<div class='status success'>✅ Otomatik görevler durduruldu</div>`;
                
            isAutoTaskRunning = false;
        }
    } catch (error) {
        document.getElementById('autoTaskStatus').innerHTML = 
            `<div class='status error'>❌ Durdurma hatası: ${error.message}</div>`;
    }
});

// Send Test Suite
document.getElementById('sendTestSuite').addEventListener('click', async () => {
    try {
        document.getElementById('autoTaskStatus').innerHTML = 
            `<div class='status'>🧪 Test paketi gönderiliyor...</div>`;
            
        const response = await fetch('/api/autotasks/test-suite', { method: 'POST' });
        
        let result = {};
        try {
            result = await response.json();
        } catch (e) {
            result = { message: "Test suite sent", taskCount: 6 };
        }
        
        if (response.ok) {
            document.getElementById('autoTaskStatus').innerHTML = 
                `<div class='status success'>✅ Test paketi gönderildi! ${result.taskCount || 6} görev oluşturuldu</div>`;
        } else {
            document.getElementById('autoTaskStatus').innerHTML = 
                `<div class='status error'>❌ Test paketi hatası: ${result.message || 'Unknown error'}</div>`;
        }
    } catch (error) {
        document.getElementById('autoTaskStatus').innerHTML = 
            `<div class='status error'>❌ Bağlantı hatası: ${error.message}</div>`;
    }
});

// Enhanced Stats Loading with AI Status
function loadEnhancedStats() {
    loadStats();
    checkAIServiceStatus();
    checkAutoTaskStatus();
}

// Update intervals
setInterval(loadEnhancedStats, 5000);
loadEnhancedStats(); // Initial load 