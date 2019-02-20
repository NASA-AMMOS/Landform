require('child_process').spawn('npm', ['run', 'build'], { stdio: 'inherit', cwd: 'client', shell: true });
