interface Props {
  size?: 'sm' | 'md' | 'lg';
  fullScreen?: boolean;
}

const sizes = {
  sm: 'h-5 w-5 border-2',
  md: 'h-8 w-8 border-2',
  lg: 'h-12 w-12 border-3',
};

export function LoadingSpinner({ size = 'lg', fullScreen = false }: Props) {
  const spinner = (
    <div className={`animate-spin rounded-full border-gray-300 border-t-blue-600 ${sizes[size]}`} />
  );

  if (fullScreen) {
    return (
      <div className="flex h-screen items-center justify-center">
        {spinner}
      </div>
    );
  }

  return (
    <div className="flex h-64 items-center justify-center">
      {spinner}
    </div>
  );
}
