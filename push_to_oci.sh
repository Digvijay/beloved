#!/bin/bash
set -e

REGISTRY="localhost:5001"

push_artifact() {
  local type=$1
  local name=$2
  local dir=$3

  echo "Pushing $type/$name to $REGISTRY..."
  
  cat <<EOF > "$dir/Dockerfile"
FROM scratch
COPY . /
EOF

  cd "$dir"
  # Use buildx or standard docker build
  docker build -t "$REGISTRY/$type/$name:latest" .
  docker push "$REGISTRY/$type/$name:latest"
  
  rm Dockerfile
  cd - > /dev/null
}

echo "Pushing templates..."
push_artifact "templates" "react-frontend" "./vault/templates/react-frontend"
push_artifact "templates" "dotnet-backend" "./vault/templates/dotnet-backend"

echo "Pushing modules..."
for mod in ./vault/modules/*; do
  if [ -d "$mod" ]; then
    mod_name=$(basename "$mod")
    push_artifact "modules" "$mod_name" "$mod"
  fi
done

echo "All artifacts pushed successfully."
