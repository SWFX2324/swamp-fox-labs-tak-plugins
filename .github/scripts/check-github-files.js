// Checks that the issue forms follow GitHub's form rules and that every YAML file under .github
// parses, so a typo doesn't silently hide a template. Run from the repository root; needs js-yaml.
const fs = require('fs');
const path = require('path');
const yaml = require('js-yaml');

const problems = [];

function load(file) {
  try {
    return yaml.load(fs.readFileSync(file, 'utf8'));
  } catch (error) {
    problems.push(`${file}: ${error.message.split('\n')[0]}`);
    return null;
  }
}

function checkForm(file, form) {
  const fail = (message) => problems.push(`${file}: ${message}`);
  for (const key of ['name', 'description', 'body']) {
    if (!form[key]) fail(`missing ${key}`);
  }
  if (!Array.isArray(form.body)) return;
  const ids = new Set();
  const labels = new Set();
  let inputs = 0;
  for (const item of form.body) {
    const attributes = item.attributes || {};
    if (item.type === 'markdown') {
      if (!attributes.value) fail('markdown block has no value');
      continue;
    }
    if (!['input', 'textarea', 'dropdown', 'checkboxes'].includes(item.type)) {
      fail(`unknown field type ${item.type}`);
      continue;
    }
    inputs++;
    if (!attributes.label) fail(`${item.type} has no label`);
    if (labels.has(attributes.label)) fail(`duplicate label ${attributes.label}`);
    labels.add(attributes.label);
    if (item.id) {
      if (!/^[A-Za-z0-9_-]+$/.test(item.id)) fail(`bad id ${item.id}`);
      if (ids.has(item.id)) fail(`duplicate id ${item.id}`);
      ids.add(item.id);
    }
    if (item.type === 'dropdown' || item.type === 'checkboxes') {
      const options = attributes.options;
      if (!Array.isArray(options) || options.length === 0) {
        fail(`${attributes.label} has no options`);
        continue;
      }
      const values = options.map((option) => (item.type === 'dropdown' ? option : option.label));
      if (values.some((value) => typeof value !== 'string' || !value)) fail(`${attributes.label} has an option that isn't text`);
      if (values.includes('None')) fail(`${attributes.label} uses the reserved option None`);
      if (new Set(values).size !== values.length) fail(`${attributes.label} has duplicate options`);
    }
  }
  if (inputs === 0) fail('has no input fields');
}

function yamlFiles(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const file = path.join(directory, entry.name);
    if (entry.isDirectory()) return yamlFiles(file);
    return /\.ya?ml$/.test(entry.name) ? [file] : [];
  });
}

const files = yamlFiles('.github');
const forms = path.join('.github', 'ISSUE_TEMPLATE');
for (const file of files) {
  const content = load(file);
  if (content && path.dirname(file) === forms && path.basename(file) !== 'config.yml') {
    checkForm(file, content);
  }
}

console.log(`Checked ${files.length} YAML files.`);
if (problems.length > 0) {
  problems.forEach((problem) => console.log(`FAIL ${problem}`));
  process.exit(1);
}
console.log('All good.');
