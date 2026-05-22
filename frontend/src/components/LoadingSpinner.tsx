interface Props {
  size?: 'sm' | 'md' | 'lg';
  fullScreen?: boolean;
  label?: string;
}

const sizes = {
  sm: 'h-4 w-4 border-2',
  md: 'h-7 w-7 border-2',
  lg: 'h-10 w-10 border-[3px]',
};

export function LoadingSpinner({ size = 'lg', fullScreen = false, label }: Props) {
  const spinner = (
    <div className="flex flex-col items-center gap-3">
      <div className={`animate-spin rounded-full border-line border-t-brand-500 ${sizes[size]}`} />
      {label && <div className="text-[12.5px] text-ink-400">{label}</div>}
    </div>
  );

  if (fullScreen) {
    return <div className="flex h-screen items-center justify-center">{spinner}</div>;
  }
  return <div className="flex h-64 items-center justify-center">{spinner}</div>;
}
