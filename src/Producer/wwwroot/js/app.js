// Load task types
fetch('/api/task-types')
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
    fetch('/api/stats')
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
        description: document.getElementById('description').value
    };

    const submitBtn = document.getElementById('submitBtn');
    const loading = document.getElementById('loading');
    const statusDiv = document.getElementById('status');

    submitBtn.disabled = true;
    loading.style.display = 'block';
    statusDiv.innerHTML = '';

    try {
        const response = await fetch('/api/send-task', {
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