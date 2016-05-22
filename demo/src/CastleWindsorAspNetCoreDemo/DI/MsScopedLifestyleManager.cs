using Castle.MicroKernel.Lifestyle;

namespace CastleWindsorAspNetCoreDemo.DI
{
    /// <summary>
    /// Extends Windsor's <see cref="ScopedLifestyleManager"/> to work as 
    /// MS style scope using <see cref="MsScopedAccesor"/>.
    /// </summary>
    public class MsScopedLifestyleManager : ScopedLifestyleManager
    {
        public MsScopedLifestyleManager()
            :base(new MsScopedAccesor())
        {
            
        }
    }
}