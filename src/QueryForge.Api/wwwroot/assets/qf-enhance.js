// Progressive enhancement for the local QueryForge upload screen.
// Never bypasses server-side limits or ETL approval.
(() => {
  const fileInput = document.getElementById('file');
  const uploadForm = document.getElementById('uploadForm');
  const fileInfo = document.getElementById('fileInfo');
  const status = document.getElementById('status');
  const progress = document.getElementById('qfUploadProgress');
  if (!fileInput || !uploadForm || !fileInfo || !status || !progress) return;

  const maxBytes = 2 * 1024 * 1024;
  const filename = file => file.name.length > 80 ? file.name.slice(0, 77) + '…' : file.name;

  function showSelection() {
    const file = fileInput.files?.[0];
    const submit = document.getElementById('uploadBtn');
    if (!file) {
      fileInfo.textContent = 'Choose a file or drag it here. Max 2 MiB / 3,000 rows / 40 columns.';
      if (submit) submit.disabled = false;
      return;
    }
    const megabytes = (file.size / (1024 * 1024)).toFixed(2);
    fileInfo.textContent = filename(file) + ' · ' + megabytes + ' MiB selected';
    const tooLarge = file.size > maxBytes || file.size === 0;
    const wrongType = !file.name.toLowerCase().endsWith('.csv');
    if (submit) submit.disabled = tooLarge || wrongType;
    if (tooLarge || wrongType) {
      status.textContent = tooLarge ? 'File must be between 1 byte and 2 MiB.' : 'Please choose a .csv file.';
      status.style.color = '#a63b43';
    } else {
      status.textContent = 'Ready to upload and check your data.';
      status.style.color = '';
    }
  }
  fileInput.addEventListener('change', showSelection);
  ['dragenter','dragover'].forEach(type => {
    fileInput.addEventListener(type, event => {
      event.preventDefault(); fileInput.classList.add('dragging');
      if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
    });
  });
  ['dragleave','drop'].forEach(type => {
    fileInput.addEventListener(type, event => {
      event.preventDefault(); fileInput.classList.remove('dragging');
    });
  });
  fileInput.addEventListener('drop', event => {
    const dropped = event.dataTransfer?.files;
    if (!dropped?.length) return;
    // File inputs support assigning a FileList from a drop event.
    fileInput.files = dropped;
    showSelection();
  });
  uploadForm.addEventListener('submit', event => {
    const file = fileInput.files?.[0];
    if (!file || file.size > maxBytes || file.size === 0 ||
        !file.name.toLowerCase().endsWith('.csv')) {
      event.preventDefault();
      showSelection();
      return;
    }
    progress.hidden = false;
  });
  const observer = new MutationObserver(() => {
    const text = status.textContent || '';
    if (/complete|error|failed/i.test(text)) progress.hidden = true;
  });
  observer.observe(status, {childList: true, characterData: true, subtree: true});

  // Keep the drop zone informative for keyboard-only users.
  fileInput.setAttribute('title','Upload a CSV file, up to 2 MiB');
})();
